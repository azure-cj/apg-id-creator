# Window fonts

The faces the window itself is set in, from the company's design system
(the same as Alpha Premier Attendance). They are built into the program as
resources, so nothing has to be installed on the office computers. They
style the window only; the printed card keeps its own fonts in `..\Fonts`.

| File | Used for | Licence | From |
| --- | --- | --- | --- |
| `Poppins-Regular.ttf` | all interface text | SIL Open Font License 1.1 (`OFL-Poppins.txt`) | google/fonts `ofl/poppins` |
| `Poppins-Medium.ttf` | labels, buttons | as above | as above |
| `Poppins-SemiBold.ttf` | emphasis | as above | as above |
| `Orbitron-Variable.ttf` | the product name and headings only | SIL Open Font License 1.1 (`OFL-Orbitron.txt`) | google/fonts `ofl/orbitron` (`Orbitron[wght].ttf`) |

Orbitron is published only as a variable font. WPF draws its default
instance (weight 400), which is the weight the headings use.

Downloaded 2026-11-23 with the owner's go-ahead; sizes match the files in
google/fonts (Poppins-Regular 160316 bytes, Medium 158576, SemiBold 157312,
Orbitron 38576).
