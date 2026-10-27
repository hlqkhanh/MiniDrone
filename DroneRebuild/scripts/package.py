"""Use ZIP-standard forward-slash paths, including on Windows."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parents[1]
archive = root / 'DroneRebuild_Simulink.zip'
with ZipFile(archive, 'w', ZIP_DEFLATED) as package:
    for file in sorted((root / 'simulation').iterdir()):
        if file.is_file() and file.suffix in {'.m', '.cpp', '.csv'}:
            package.write(file, 'DroneRebuild/simulation/' + file.name)
    package.write(root / 'firmware/DroneRebuild/RollCore.h', 'DroneRebuild/firmware/DroneRebuild/RollCore.h')
    package.write(root / 'docs/SIMULINK.md', 'DroneRebuild/HUONG_DAN.md')
print(archive)
