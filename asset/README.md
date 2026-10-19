# Drone model

- `esp32_drone_3d_model.glb`: original model supplied by the user.
- `esp32_drone_3d_model.viewer.glb`: optimized model used by the WPF viewer (97,293 triangles, down from 1,945,873).

The project copies the viewer asset to `Assets/drone.glb` on build and publish. It does not require the original asset at runtime.

To regenerate the viewer asset with [gltfpack 1.3](https://github.com/zeux/meshoptimizer/releases/tag/v1.3), run from the repository root:

```powershell
gltfpack -i assert/esp32_drone_3d_model.glb -o assert/esp32_drone_3d_model.viewer.glb -si 0.05 -se 0.002 -sp -noq
```

The viewer loads the static scene, node transforms, normals, UVs, and base-color texture using SharpGLTF. It preserves glTF's Y-up coordinates, centers the model at its bounding-box center, and scales its longest dimension to 6 viewer units before applying the sensor quaternion. The USB end points along model +Z; the default camera is on +Z so it faces the USB end. The mounted sensor axes X/Y/Z map to model -X/+Z/+Y: positive sensor X lifts the USB end, positive sensor Y banks right, and sensor Z turns around the vertical axis. On every connection, the first valid sample establishes the reference pose; Zero orientation replaces it with the current pose. Hold the drone level at connection time to establish a level reference.

WPF renders the base-color texture with diffuse lighting; glTF metallic/roughness and normal maps are not rendered. Textures are decoded at up to 2048 pixels wide. The original GLB is retained for future rendering or optimization changes.
