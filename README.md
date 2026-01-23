# ResoniteComponentSchemaGenerator

A .NET tool that analyzes FrooxEngine.dll and generates JSON Schema files for Resonite components. The schemas are compatible with [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink)'s `addComponent` and `updateComponent` websocket commands.

## Features

- List all `Component`-derived classes from FrooxEngine.dll
- Filter components by name pattern
- Inspect public fields of any component
- Generate JSON Schema files for individual or all components
- Proper type mapping for FrooxEngine wrapper types (`Sync<T>`, `SyncList<T>`, `SyncRef<T>`, etc.)
- Support for primitives, vectors, colors, quaternions, enums, and references
- Support for nullable types (`bool?`, `int?`, etc.) with proper optional value handling
- Support for generic components with `[GenericTypes]` attribute (e.g., `ValueField<T>`)
- Schemas use `$ref` for shared type definitions to reduce duplication

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
  Global: Sync<Nullable<Boolean>>
  Volume: Sync<Single>
  ...
```

### Show fields of a generic component

Generic components can be referenced using `<1>` syntax for the type arity:

```bash
dotnet run -- /path/to/FrooxEngine.dll -p "ValueField<1>"
```

Output:
```
Component: FrooxEngine.ValueField`1
Abstract: False
Generic: Yes (with type constraints)

Allowed types for T (60):
  Elements.Core.bool2
  Elements.Core.color
  Elements.Core.float3
  System.Boolean
  System.Int32
  System.Single
  System.String
  ...

Inheritance chain:
  FrooxEngine.ValueField`1
    FrooxEngine.Component

Public fields:

  Value: Sync<T>
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

Generated schemas use `$ref` to shared type definitions in `$defs`, reducing duplication when multiple fields share the same type.

### AudioOutput Example

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "AudioOutput",
  "type": "object",
  "properties": {
    "componentType": {
      "const": "[FrooxEngine]FrooxEngine.AudioOutput"
    },
    "members": {
      "properties": {
        "Volume": { "$ref": "#/$defs/float_value" },
        "Pitch": { "$ref": "#/$defs/float_value" },
        "MaxDistance": { "$ref": "#/$defs/float_value" },
        "Spatialize": { "$ref": "#/$defs/bool_value" },
        "Global": { "$ref": "#/$defs/nullable_bool_value" },
        "AudioTypeGroup": { "$ref": "#/$defs/AudioTypeGroup_value" }
      }
    }
  },
  "$defs": {
    "float_value": {
      "type": "object",
      "properties": {
        "$type": { "const": "float" },
        "value": { "type": "number" }
      },
      "required": ["$type", "value"]
    },
    "bool_value": {
      "type": "object",
      "properties": {
        "$type": { "const": "bool" },
        "value": { "type": "boolean" }
      },
      "required": ["$type", "value"]
    },
    "nullable_bool_value": {
      "type": "object",
      "properties": {
        "$type": { "const": "bool?" },
        "value": { "type": ["boolean", "null"] }
      },
      "required": ["$type"]
    },
    "AudioTypeGroup_value": {
      "type": "object",
      "properties": {
        "$type": { "const": "string" },
        "value": {
          "type": "string",
          "enum": ["SoundEffect", "Multimedia", "Voice", "UI"]
        }
      },
      "required": ["$type", "value"]
    }
  }
}
```

### ValueField<T> Example (Generic Component)

Generic components with `[GenericTypes]` attribute generate a schema with `oneOf` containing all valid type instantiations:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "ValueField",
  "description": "ResoniteLink schema for FrooxEngine.ValueField`1 with 60 type variant(s) for T",
  "oneOf": [
    { "$ref": "#/$defs/ValueField_bool" },
    { "$ref": "#/$defs/ValueField_int" },
    { "$ref": "#/$defs/ValueField_float" },
    { "$ref": "#/$defs/ValueField_string" },
    { "$ref": "#/$defs/ValueField_float3" },
    { "$ref": "#/$defs/ValueField_color" },
    ...
  ],
  "$defs": {
    "bool_value": {
      "type": "object",
      "properties": {
        "$type": { "const": "bool" },
        "value": { "type": "boolean" }
      },
      "required": ["$type", "value"]
    },
    "float3_value": {
      "type": "object",
      "properties": {
        "$type": { "const": "float3" },
        "value": {
          "type": "object",
          "properties": {
            "x": { "type": "number" },
            "y": { "type": "number" },
            "z": { "type": "number" }
          },
          "required": ["x", "y", "z"]
        }
      },
      "required": ["$type", "value"]
    },
    "ValueField_bool": {
      "type": "object",
      "title": "ValueField<bool>",
      "properties": {
        "componentType": {
          "const": "[FrooxEngine]FrooxEngine.ValueField<bool>"
        },
        "members": {
          "properties": {
            "Value": { "$ref": "#/$defs/bool_value" }
          }
        }
      }
    },
    "ValueField_float3": {
      "type": "object",
      "title": "ValueField<float3>",
      "properties": {
        "componentType": {
          "const": "[FrooxEngine]FrooxEngine.ValueField<float3>"
        },
        "members": {
          "properties": {
            "Value": { "$ref": "#/$defs/float3_value" }
          }
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
| `Sync<Boolean?>` | `"bool?"` | `["boolean", "null"]` (value optional) |
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
