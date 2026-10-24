## On Linux: the black squares (#720, note 1017)

If the game draws black squares or rectangles over the screen (the title screen's hills and trees, the train, the smoke):

1. Quit, then start it from a terminal in the game's folder with the safe path on: `./DarkTerritory --gpu-safe`
   (or in the game: **SETTINGS**, **SAFE GRAPHICS: ON**, then start it again). Do the squares go?
2. Either way, send back:
   - [ ] `vulkaninfo --summary` (Ubuntu and Debian: `sudo apt install vulkan-tools`; Fedora: `vulkan-tools`; Arch:
         `vulkan-tools`). If you can't install it: `lspci -k | grep -A3 -i vga`.
   - [ ] `echo $XDG_SESSION_TYPE` (`wayland` or `x11`), and whether the game was a window or the whole screen.
   - [ ] A **REPORT A PROBLEM** from the title screen (it says your GPU and driver), sent with the mail it opens.
   - [ ] A screenshot, and whether the squares are there on the title screen before any night, and whether they move with
         the camera or stay put on the screen.
   - [ ] Whether `--gpu-safe` cleared them, made them fewer, or changed nothing.
   - [ ] The first lines the game prints in the terminal that start with `gpu:` (it says there if it had to make the
         textures smaller to fit the GPU).
