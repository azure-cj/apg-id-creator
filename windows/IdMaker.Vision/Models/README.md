# The background-removal model

`modnet.onnx` is fetched here by the build (see `IdMaker.Vision.csproj`)
and is not kept in git.

- **What:** MODNet, "photographic portrait matting": it finds the person
  in an ordinary photograph and returns how much of each pixel is them,
  which keeps hair edges soft instead of cutting them square.
- **Licence:** Apache-2.0 (ZHKKKe/MODNet). The ONNX export is the one
  published as `Xenova/modnet`.
- **Size and speed:** 25 MB; about a third of a second per photo on an
  ordinary laptop processor. No graphics card needed.
- **Pinned:** the build refuses any file whose SHA-256 is not the one in
  the project file, so a changed download cannot slip in unnoticed.

It runs entirely on this computer. No photograph is sent anywhere.

What it is good at, measured on the repository's sample portrait pasted
over test backgrounds: plain and softly varied walls and office
backgrounds come out with 99 % overlap against the true outline. What it
is not good at: bold stripes or busy printed artwork right behind the
person (73–85 %). A photo taken against a wall is the case it is for.
