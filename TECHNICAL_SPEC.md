# LilyPad GH - Technical Specification

## Executive Summary

LilyPad GH is a Grasshopper plugin for Rhino 3D that enables 2D Computational Fluid Dynamics (CFD) analysis directly within the Grasshopper visual programming environment. The plugin integrates with the WaterLily.jl solver through an HTTP server architecture, providing real-time fluid flow simulation capabilities with an interactive Eto.Forms UI for parameter control.

## System Architecture

### Technology Stack

- **Frontend**: Grasshopper/Rhino 3D Plugin (.gha)
- **Programming Language**: C# (.NET 8.0 Windows)
- **UI Framework**: Eto.Forms (cross-platform UI)
- **Backend Solver**: Julia with WaterLily.jl
- **Communication**: HTTP REST API (localhost:8080)
- **Build System**: MSBuild with custom deployment scripts
- **Target Platform**: Windows (Rhino 7/8)

### Component Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Rhino/Grasshopper                     │
├─────────────────────────────────────────────────────────┤
│                   LilyPad GH Plugin                      │
│  ┌──────────────┐  ┌─────────────┐  ┌──────────────┐  │
│  │  Component   │  │  Eto Dialog │  │  Attributes  │  │
│  │   (Main)     │◄─┤   (UI)      │  │  (Canvas)    │  │
│  └──────┬───────┘  └─────────────┘  └──────────────┘  │
│         │                                               │
│  ┌──────▼───────┐  ┌─────────────┐  ┌──────────────┐  │
│  │   Settings   │  │  JSON Post  │  │ Julia Runner │  │
│  │   (Model)    │  │  Component  │  │  Component   │  │
│  └──────────────┘  └──────┬──────┘  └──────┬───────┘  │
└─────────────────────────────┼───────────────┼──────────┘
                              │               │
                    HTTP API  │               │ Process
                   Port 8080  │               │ Control
                              ▼               ▼
                 ┌────────────────────────────────┐
                 │    Julia Server (RunServer.jl) │
                 │  ┌────────────────────────┐   │
                 │  │   WaterLily.jl Solver  │   │
                 │  └────────────────────────┘   │
                 └────────────────────────────────┘
```

## Core Components

### 1. Main Grasshopper Component (`LilyPad.cs`)

**Purpose**: Core integration point with Grasshopper canvas

**Key Features**:
- Manages component inputs/outputs
- Handles boundary rectangle and curve geometry processing
- Coordinates with Eto dialog for parameter settings
- Manages Julia server lifecycle
- Performs curve discretization and JSON serialization

**Inputs**:
- Boundary Plane (Rectangle3d) - Required
- Custom Curves (List<Curve>) - Optional
- Julia Path (string) - Optional custom Julia installation path

**Outputs**:
- Status (string) - Current analysis status
- Parameters (string) - Simulation parameters summary
- Boundary Rectangle (Rectangle3d)
- Boundary JSON (string)
- Curves JSON (string) - Multi-polyline format
- Curve Points (List<Point3d>) - Discretized points
- Julia Output (string) - Server response

### 2. Eto Forms Dialog (`LilyPad_EtoDialog.cs`)

**Purpose**: Interactive UI for simulation configuration

**Features**:
- Reynolds Number slider (0-1000)
- Flow velocity control (0-5)
- Grid resolution settings (X/Y: 32-256)
- Simulation duration control (1-60 seconds)
- Curve discretization divisions (10-200)
- Julia-specific parameters:
  - Simulation scale (L)
  - Animation duration
  - Body plotting toggle
  - Point simplification controls

**Control Buttons**:
- Start/Stop Server - Julia process management
- Apply Parameters & Run - Push data to server
- Run/Stop Analysis - Local simulation control

### 3. Custom Canvas Attributes (`LilyPad_Attributes.cs`)

**Purpose**: Enhanced component visualization on Grasshopper canvas

**Features**:
- Custom "Configure & Run" button overlay
- Visual state indicators (Ready/Running/Server Active)
- Direct dialog launch from canvas
- Custom rendering with status colors

### 4. Settings Model (`LilyPad_Settings.cs`)

**Data Structure**:
```csharp
class LilyPadCfdSettings {
    // Flow properties
    double ReynoldsNumber (default: 250)
    double Velocity (default: 1.0)

    // Grid settings
    int GridResolutionX (default: 192)
    int GridResolutionY (default: 128)
    double Duration (default: 10.0 seconds)

    // Geometry
    int CurveDivisions (default: 50)

    // Julia parameters
    int SimulationL (default: 32)
    double SimulationU (default: 1.0)
    double AnimationDuration (default: 1.0)
    bool PlotBody (default: true)
    double SimplifyTolerance (default: 0.0)
    int MaxPointsPerPoly (default: 1000)
}
```

### 5. Helper Components

#### PostJson Component (`PostJson.cs`)
- Standalone HTTP POST functionality
- Generic JSON data posting to specified endpoints
- Used for testing and debugging server communication

#### StartStopJulia Component (`StartStopJulia.cs`)
- Simplified Julia server management
- Toggle-based server control
- Status monitoring

## Julia Integration

### Server Architecture (`RunServer.jl`)

**HTTP Endpoints**:
- `POST /` or `/process` - Process simulation data
- `GET /status` - Server health check
- `GET /shutdown` - Graceful server shutdown

**Process Flow**:
1. Server starts on localhost:8080
2. Receives JSON payload with geometry and parameters
3. Parses polyline data
4. Configures WaterLily solver
5. Runs 2D CFD simulation
6. Generates animation (GIF output)
7. Returns completion status

### Simulation Scripts

- `2d.jl` - Core 2D simulation logic with WaterLily
- `2d_MultiBody.jl` - Multi-body simulation support
- `3d.jl` - 3D simulation capabilities (experimental)

## Data Flow

### Geometry Processing Pipeline

1. **Input Stage**:
   - Receive boundary rectangle from Grasshopper
   - Collect optional custom curves

2. **Discretization**:
   - Smart polyline detection
   - Straight segment optimization
   - Configurable division count
   - Closed curve handling

3. **Serialization**:
   ```json
   {
     "type": "multiple_closed_polylines",
     "count": 2,
     "polylines": [
       {
         "type": "closed_polyline",
         "is_closed": true,
         "divisions": 50,
         "points": [{"x": 0, "y": 0, "z": 0}, ...]
       }
     ]
   }
   ```

4. **Server Communication**:
   - HTTP POST to Julia server
   - JSON payload with simulation parameters
   - Asynchronous processing
   - Status feedback to UI

## Deployment

### Build Configurations

- **Debug**: Development builds without optimization
- **Release**: Production builds with optimization
- **Deploy Rhino 7/8**: Target-specific deployments
- **VS Debug/Release**: Visual Studio Professional compatibility

### Package Structure

```
%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\
├── LilyPadGH.gha          # Main plugin assembly
├── Icons\                 # Embedded resources
│   └── CFDAnalysisIcon.png
├── Julia\                 # Julia scripts
│   ├── RunServer.jl
│   ├── 2d.jl
│   ├── 2d_MultiBody.jl
│   └── 3d.jl
└── manifest.yml          # Package manifest
```

### Dependencies

**NuGet Packages**:
- Grasshopper 8.1.23325.13001
- RhinoCommon 8.1.23325.13001
- System.Text.Json 8.0.5
- System.Drawing.Common 8.0.0

**Julia Requirements**:
- Julia 1.11.x
- WaterLily.jl package
- HTTP.jl package
- Additional scientific computing packages

## Performance Characteristics

### Optimization Features

- **Asynchronous Processing**: Non-blocking UI during simulations
- **Smart Curve Discretization**: Automatic straight segment detection
- **Point Reduction**: Douglas-Peucker simplification (optional)
- **Parallel Processing**: Julia multi-threading support
- **Caching**: Component state preservation

### Resource Usage

- **Memory**: ~200-500MB for typical simulations
- **CPU**: Multi-threaded Julia solver utilization
- **Network**: Localhost HTTP communication only
- **Storage**: Temporary GIF outputs (~5-20MB)

## Error Handling

### Component Level
- Input validation with runtime messages
- Graceful null/invalid geometry handling
- Clear error reporting to Grasshopper canvas

### Server Communication
- Timeout handling (30 seconds default)
- Connection failure recovery
- Process crash detection
- Automatic cleanup on shutdown

### Julia Process
- Path detection fallbacks
- Version compatibility checks
- Missing dependency warnings
- Server startup verification

## Security Considerations

- **Localhost Only**: No external network exposure
- **Process Isolation**: Julia runs as separate process
- **Input Validation**: Geometry and parameter sanitization
- **No File System Access**: Beyond configured paths
- **Clean Shutdown**: Proper resource cleanup

## Future Enhancements

### Planned Features
- 3D CFD simulation support
- Real-time visualization in Rhino viewport
- Multi-physics coupling (heat transfer)
- Cloud computation offloading
- Result data export formats

### Technical Improvements
- WebSocket for live updates
- GPU acceleration support
- Distributed computing
- Advanced mesh generation
- Interactive parameter tuning

## API Reference

### Component Methods

```csharp
// Main component lifecycle
protected override void RegisterInputParams(GH_InputParamManager pManager)
protected override void RegisterOutputParams(GH_OutputParamManager pManager)
protected override void SolveInstance(IGH_DataAccess DA)

// UI interaction
public void ShowConfigDialog()
private void HandleRunClicked(LilyPadCfdSettings settings)
private void HandleStartServerClicked()
private void HandleApplyParametersClicked(LilyPadCfdSettings settings)

// Julia integration
private string GetJuliaExecutablePath()
private string GetServerScriptPath()
private async Task RunSimulationAsync(CancellationToken token)
```

### HTTP API

```http
POST / HTTP/1.1
Content-Type: application/json

{
  "simulation_parameters": {
    "reynolds_number": 250,
    "velocity": 1.0,
    "grid_resolution_x": 192,
    "grid_resolution_y": 128,
    "L": 32,
    "U": 1.0,
    "Re": 250,
    "animation_duration": 1.0,
    "plot_body": true
  },
  "polylines": [...]
}
```

## Testing

### Unit Testing Approach
- Component input/output validation
- Geometry processing verification
- JSON serialization testing
- Settings model cloning

### Integration Testing
- Julia server communication
- End-to-end simulation runs
- UI interaction workflows
- Multi-component scenarios

### Performance Testing
- Large geometry handling
- Long-duration simulations
- Memory leak detection
- Thread safety verification

## Documentation

### User Documentation
- README.md - Quick start guide
- Component help strings
- Parameter tooltips
- Example Grasshopper definitions

### Developer Documentation
- Inline code comments
- Architecture diagrams
- API documentation
- Build instructions

## Support and Maintenance

### Version Control
- Git repository structure
- Branch strategy (main, development, feature branches)
- Semantic versioning (0.0.1)

### Issue Tracking
- GitHub Issues integration
- Bug report templates
- Feature request process

### Release Process
1. Version increment in .csproj
2. Update manifest.yml
3. Build all configurations
4. Package with deployment scripts
5. GitHub release creation
6. Rhino package manager submission

## License

MIT License - Open source with attribution requirement

## Contact and Resources

- **Repository**: https://github.com/[organization]/lily-pad-gh
- **Documentation**: Inline and README
- **Support**: GitHub Issues
- **Dependencies**:
  - WaterLily.jl: https://github.com/WaterLily-jl/WaterLily.jl
  - Rhino/Grasshopper: https://www.rhino3d.com/

---

*Technical Specification Version 1.0 - Generated on 2025-09-21*