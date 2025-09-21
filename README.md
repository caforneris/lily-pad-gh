
README.md
====================================================

# LilyPad GH

LilyPad is a **Grasshopper plugin** for Rhino 3D that enables **2D Computational Fluid Dynamics (CFD) analysis** inside Grasshopper.  
It integrates with the [WaterLily](https://github.com/WaterLily-jl/WaterLily.jl) solver (Julia-based) and provides an interactive Eto.Forms UI for controlling simulation parameters.

---

## Features

- 📐 **Grasshopper Component**  
  Add the `LilyPad CFD Analysis` component to your Grasshopper canvas. It takes a boundary rectangle and geometry input, which includes both 2D curve and 3D geometry input and outputs:
  - Simulation status
  - Parameters summary
  - Boundary geometry (Rhino + JSON)
  - Curve discretisation (points + JSON)
  - Results from an external Julia script

- ⚙️ **Customisable UI (Eto Dialog)**  
  Configure CFD settings via an interactive Eto.Forms dialog:
  - Reynolds Number
  - Flow velocity
  - Grid resolution (X, Y)
  - Simulation duration
  - Curve discretisation divisions

- 🖥 **Background Simulation**  
  Runs asynchronously, with live status updates displayed on the component and dialog.

- 🟦 **Custom Canvas UI**  
  A `Configure & Run` button is embedded directly on the Grasshopper canvas, opening the settings dialog.

- 🐍 **Julia Integration**  
  LilyPad bundles a standalone Julia runtime and executes scripts (e.g. `simple_script.jl`) using the `JuliaRunner` helper class.

---

## Project Structure

