# The pen tablet driver the installer adds: XP-Pen's own, from XP-Pen's own
# server, pinned to one version and one exact file.
#
# To move to a newer driver: download it from
# https://www.xp-pen.com/download/deco-fun.html, put its address, file name
# and SHA-256 (Get-FileHash) here, and install it once by hand on a test
# computer before sending it to everyone.
@{
    Name    = 'XP-Pen Deco Fun driver'
    Version = '4.0.21.261022'
    Url     = 'https://download01.xp-pen.com/file/2026/10/XPPenWin_4.0.21.261022.zip'
    Zip     = 'XPPenWin_4.0.21.261022.zip'
    Exe     = 'XPPenWin_4.0.21.261022.exe'
    Sha256  = '7a72c17038b72af5b4ad55c6a03c25b3dd16464299b621ff554b39396a3dc9ba'
}
