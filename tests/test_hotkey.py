import unittest

from primeshift.hotkey import MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN, parse_hotkey


class ParseHotkeyTest(unittest.TestCase):
    def test_function_keys(self):
        self.assertEqual(parse_hotkey("ctrl+alt+f11"), (MOD_CONTROL | MOD_ALT, 0x7A))
        self.assertEqual(parse_hotkey("Ctrl + Alt + F12"), (MOD_CONTROL | MOD_ALT, 0x7B))

    def test_letters_digits_and_named_keys(self):
        self.assertEqual(parse_hotkey("win+shift+k"), (MOD_WIN | MOD_SHIFT, ord("K")))
        self.assertEqual(parse_hotkey("ctrl+1"), (MOD_CONTROL, ord("1")))
        self.assertEqual(parse_hotkey("ctrl+numpad5"), (MOD_CONTROL, 0x65))
        self.assertEqual(parse_hotkey("alt+pagedown"), (MOD_ALT, 0x22))

    def test_invalid(self):
        for text in ("f11", "ctrl+", "hyper+f1", "ctrl+f25", "ctrl+foo"):
            with self.assertRaises(ValueError, msg=text):
                parse_hotkey(text)


if __name__ == "__main__":
    unittest.main()
