import ctypes
import os
import tempfile
import unittest
from unittest import mock

from primeshift import ccd, profile


def make_luid(low, high=0):
    return ccd.LUID(low, high)


def make_path(adapter, source_id, target_id, source_mode_idx, active=True, available=True):
    path = ccd.DISPLAYCONFIG_PATH_INFO()
    path.sourceInfo.adapterId = adapter
    path.sourceInfo.id = source_id
    path.sourceInfo.modeInfoIdx = source_mode_idx
    path.targetInfo.adapterId = adapter
    path.targetInfo.id = target_id
    path.targetInfo.modeInfoIdx = ccd.DISPLAYCONFIG_PATH_MODE_IDX_INVALID
    path.targetInfo.refreshRate.Numerator = 165000
    path.targetInfo.refreshRate.Denominator = 1000
    path.targetInfo.targetAvailable = int(available)
    path.flags = ccd.DISPLAYCONFIG_PATH_ACTIVE if active else 0
    return path


def make_source_mode(adapter, source_id, width, height, x, y):
    mode = ccd.DISPLAYCONFIG_MODE_INFO()
    mode.infoType = ccd.MODE_INFO_TYPE_SOURCE
    mode.id = source_id
    mode.adapterId = adapter
    mode.sourceMode.width = width
    mode.sourceMode.height = height
    mode.sourceMode.position.x = x
    mode.sourceMode.position.y = y
    return mode


NAMES = {0: ("Main 5K", r"\\?\DISPLAY#MAIN"), 3: ("GL-RM10", r"\\?\DISPLAY#KVM")}


def target_name(_luid, target_id):
    return NAMES[target_id]


class StructLayoutTest(unittest.TestCase):
    def test_sizes_match_wingdi(self):
        self.assertEqual(ctypes.sizeof(ccd.DISPLAYCONFIG_PATH_INFO), 72)
        self.assertEqual(ctypes.sizeof(ccd.DISPLAYCONFIG_MODE_INFO), 64)
        self.assertEqual(ctypes.sizeof(ccd.DISPLAYCONFIG_DEVICE_INFO_HEADER), 20)
        # WCHAR is 2 bytes on Windows (84 / 420 / 276) but 4 bytes elsewhere.
        w = ctypes.sizeof(ctypes.c_wchar)
        self.assertEqual(ctypes.sizeof(ccd.DISPLAYCONFIG_SOURCE_DEVICE_NAME), 20 + 32 * w)
        self.assertEqual(ctypes.sizeof(ccd.DISPLAYCONFIG_TARGET_DEVICE_NAME), 36 + 192 * w)
        self.assertEqual(ctypes.sizeof(ccd.DISPLAYCONFIG_ADAPTER_NAME), 20 + 128 * w)


class DescribeTest(unittest.TestCase):
    def test_primary_is_display_at_origin(self):
        gpu = make_luid(0x1234)
        paths = [make_path(gpu, 0, 0, 0), make_path(gpu, 1, 3, 1)]
        modes = [
            make_source_mode(gpu, 0, 5120, 2880, 0, 0),
            make_source_mode(gpu, 1, 1920, 1080, 5120, 0),
        ]
        main, kvm = profile.describe_paths(paths, modes, target_name)
        self.assertTrue(main.primary)
        self.assertEqual((main.width, main.height, main.refresh_hz), (5120, 2880, 165.0))
        self.assertFalse(kvm.primary)
        self.assertEqual((kvm.name, kvm.x), ("GL-RM10", 5120))


class ProfileRoundTripTest(unittest.TestCase):
    def setUp(self):
        self.gpu = make_luid(0x1234)
        self.paths = [make_path(self.gpu, 1, 3, 0)]
        self.modes = [make_source_mode(self.gpu, 1, 1920, 1080, 0, 0)]
        monitors = profile.describe_paths(self.paths, self.modes, target_name)
        self.profile = profile.build_profile(
            self.paths, self.modes, lambda luid: r"\\?\PCI#GPU", monitors
        )

    def test_decode_restores_raw_records(self):
        paths, modes = profile.decode_profile(self.profile)
        self.assertEqual(bytes(paths[0]), bytes(self.paths[0]))
        self.assertEqual(bytes(modes[0]), bytes(self.modes[0]))
        self.assertEqual(self.profile["adapters"], {"00000000:00001234": r"\\?\PCI#GPU"})

    def test_apply_remaps_luid_after_reboot(self):
        new_gpu = make_luid(0x9999, 1)
        current = [make_path(new_gpu, 1, 3, 0, active=False)]
        calls = []
        with mock.patch.object(ccd, "query_config", return_value=(current, [])), \
             mock.patch.object(ccd, "get_adapter_path", return_value=r"\\?\PCI#GPU"), \
             mock.patch.object(ccd, "set_config", side_effect=lambda p, m, f: calls.append((p, m, f))):
            profile.apply(self.profile)

        self.assertEqual(len(calls), 2)
        self.assertTrue(calls[0][2] & ccd.SDC_VALIDATE)
        self.assertTrue(calls[1][2] & ccd.SDC_APPLY)
        paths, modes, _ = calls[1]
        for luid in (paths[0].sourceInfo.adapterId, paths[0].targetInfo.adapterId, modes[0].adapterId):
            self.assertEqual(profile.luid_key(luid), "00000001:00009999")

    def test_apply_rejects_unknown_adapter(self):
        with mock.patch.object(ccd, "query_config", return_value=([make_path(self.gpu, 1, 3, 0)], [])), \
             mock.patch.object(ccd, "get_adapter_path", return_value=r"\\?\PCI#OTHER"), \
             mock.patch.object(ccd, "set_config") as set_config:
            with self.assertRaisesRegex(profile.ProfileError, "显卡"):
                profile.apply(self.profile)
        set_config.assert_not_called()

    def test_apply_rejects_disconnected_monitor(self):
        current = [make_path(self.gpu, 1, 3, 0, available=False)]
        with mock.patch.object(ccd, "query_config", return_value=(current, [])), \
             mock.patch.object(ccd, "get_adapter_path", return_value=r"\\?\PCI#GPU"), \
             mock.patch.object(ccd, "set_config") as set_config:
            with self.assertRaisesRegex(profile.ProfileError, "GL-RM10"):
                profile.apply(self.profile)
        set_config.assert_not_called()

    def test_save_and_load(self):
        with tempfile.TemporaryDirectory() as tmp, mock.patch.dict(os.environ, {"PRIMESHIFT_HOME": tmp}):
            profile.save("remote", self.profile)
            self.assertEqual(profile.load("remote"), self.profile)
            self.assertEqual(profile.saved_names(), ["remote"])
            with self.assertRaises(profile.ProfileError):
                profile.save("remote", self.profile)
            profile.save("remote", self.profile, overwrite=True)

    def test_rejects_unsafe_names(self):
        for name in ("../evil", "a b", ""):
            with self.assertRaises(profile.ProfileError):
                profile.profile_path(name)


if __name__ == "__main__":
    unittest.main()
