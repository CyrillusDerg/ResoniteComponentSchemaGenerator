# ResoniteComponentSchemaGenerator

A .NET tool that analyzes FrooxEngine.dll and generates JSON Schema files for Resonite components. The schemas are compatible with [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink)'s `addComponent` and `updateComponent` websocket commands.

## Features

- List all `Component`-derived classes from FrooxEngine.dll
- Filter components by name pattern
- Inspect public fields of any component
- Generate JSON Schema files for individual or all components
- Proper type mapping for FrooxEngine wrapper types (`Sync<T>`, `SyncList<T>`, `SyncRef<T>`, etc.)
- Support for primitives, vectors, colors, quaternions, enums, and references

## Requirements

- .NET 10.0 SDK
- FrooxEngine.dll and its dependencies (from a Resonite installation)

## Building

```bash
dotnet build
```

## Usage

```
ComponentAnalyzer <path-to-DLL> [options]

Options:
  -l, --list [pattern]   List components, optionally filtered by pattern
  -p, --props <class>    Show public fields of a component
  -s, --schema [class]   Generate JSON schema (for specific class or all)
  -o, --output <dir>     Output directory for schema files (default: current)
  -h, --help             Show this help message
```

## Examples

### List all components

```bash
dotnet run -- /path/to/FrooxEngine.dll
```

### List components containing "Audio"

```bash
dotnet run -- /path/to/FrooxEngine.dll -l Audio
```

### Show fields of a specific component

```bash
dotnet run -- /path/to/FrooxEngine.dll -p AudioOutput
```

Output:
```
Component: FrooxEngine.AudioOutput
Abstract: False

Inheritance chain:
  FrooxEngine.AudioOutput
    FrooxEngine.Component

Public fields:

  AudioTypeGroup: Sync<AudioTypeGroup>
  DistanceSpace: Sync<AudioDistanceSpace>
  DopplerLevel: Sync<Single>
  Volume: Sync<Single>
  ...
```

### Generate schema for a specific component

```bash
dotnet run -- /path/to/FrooxEngine.dll -s AudioOutput -o ./schemas
```

### Generate schemas for all components

```bash
dotnet run -- /path/to/FrooxEngine.dll -s -o ./schemas
```

## Schema Format

The generated schemas are compatible with ResoniteLink's data format. Each field uses a `$type` discriminator and a `value` property:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "AudioOutput",
  "description": "ResoniteLink schema for FrooxEngine.AudioOutput",
  "type": "object",
  "properties": {
    "componentType": {
      "const": "[FrooxEngine]FrooxEngine.AudioOutput"
    },
    "members": {
      "type": "object",
      "properties": {
        "Volume": {
          "type": "object",
          "properties": {
            "$type": { "const": "float" },
            "value": { "type": "number" }
          },
          "required": ["$type", "value"]
        }
      }
    }
  }
}
```

## Type Mappings

| FrooxEngine Type | ResoniteLink `$type` | Value Schema |
|------------------|---------------------|--------------|
| `Sync<Boolean>` | `"bool"` | `boolean` |
| `Sync<Int32>` | `"int"` | `integer` |
| `Sync<Single>` | `"float"` | `number` |
| `Sync<String>` | `"string"` | `string` |
| `Sync<colorX>` | `"colorX"` | `{r, g, b, a}` |
| `Sync<float3>` | `"float3"` | `{x, y, z}` |
| `Sync<floatQ>` | `"floatQ"` | `{x, y, z, w}` |
| `Sync<Enum>` | `"string"` | `string` with enum values |
| `SyncRef<T>` | `"reference"` | `{targetId, targetType}` |
| `AssetRef<T>` | `"reference"` | `{targetId, targetType}` |
| `FieldDrive<T>` | `"reference"` | `{targetId, targetType}` |
| `SyncList<T>` | `"syncList"` | `{elements: [...]}` |
| `SyncRefList<T>` | `"syncList"` | `{elements: [{reference}, ...]}` |

## License

MIT
