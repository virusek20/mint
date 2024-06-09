# MGS Import .NET Tool
MINT is a CLI archive manipulation and model / texture conversion program for Metal Gear Solid.

## Usage
The program uses autogenerated commandline parsing, so until I get a proper readme done you can just run `MetalMintSolid.exe` in CMD / Powershell and look at the output.

In general you want to extract `stage.mgz` in your game directory (it's just a regular .zip file so any standard tool will do), and then unpack the DAR achives inside.

For reference the main snake model is located in `init_jim` with textures being in `res_tex1.dar` and models in `res_mdl1.dar`.

// TODO: Actual command showcase, model swap guide, texture swap guide

## Build instructions
- Open `MetalMintSolid.sln` in preferred IDE
- Build the only project in solution
Currently only Windows is supported due to System.Drawing usage

## Tested platforms
- PC - Original CD Version
- PC - GOG

## File formats
### Supported MGS file formats
- DAR (Archive)
- PCX (Image / texture)
- KMD (Model)

### Supported input file formats
- GLTF (Model)
- PNG, JPG, probably a lot more (Image / texture)

## References
- https://github.com/FoxdieTeam/mgs_reversing/tree/master
- https://www.mgsdevwiki.com/wiki/index.php
- https://mgs.w00ty.com/mgs1/launcher
- https://sketchfab.com/scuffward (Testing models)