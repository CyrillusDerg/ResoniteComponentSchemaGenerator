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
- Handles `[NameOverride]` attribute for correct serialized field names
- Includes base component fields (`persistent`, `UpdateOrder`, `Enabled`) from inheritance chain
- Schemas use `$ref` for shared type definitions to reduce duplication
- All member values include required `id` field for ResoniteLink compatibility
- Component schemas include required `id` and `isReferenceOnly` fields

## Requirements

- .NET 10.0 SDK
- FrooxEngine.dll and its dependencies (from a Resonite installation)

## Building

```bash
dotnet build docgen.sln
```

## Testing

The project includes unit tests that verify schema generation against real FrooxEngine.dll output.

### Running Tests

```bash
dotnet test docgen.sln
```

### Test Configuration

By default, tests look for FrooxEngine.dll at:

```txt
F:\Steam\steamapps\common\Resonite\FrooxEngine.dll
```

To use a different location, set the `FROOXENGINE_DLL_PATH` environment variable:

```bash
# Windows (PowerShell)
$env:FROOXENGINE_DLL_PATH = "C:\Path\To\FrooxEngine.dll"
dotnet test docgen.sln

# Windows (CMD)
set FROOXENGINE_DLL_PATH=C:\Path\To\FrooxEngine.dll
dotnet test docgen.sln

# Linux/macOS
FROOXENGINE_DLL_PATH=/path/to/FrooxEngine.dll dotnet test docgen.sln
```

### Test Coverage

The tests verify:

- **AudioOutput schema**: Metadata, componentType format, field types (float, int, bool, nullable bool), enum definitions, reference fields, sync lists, `id` field requirements
- **ValueField\<T\> schema**: oneOf structure for generic components, type variants (bool, int, float, string, float3, color), vector/quaternion/color value schemas
- **ComponentLoader**: Finding components by name, generic syntax normalization (`ValueField<1>`, `ValueField[1]`, `ValueField<T>`), inheritance chains
- **PropertyAnalyzer**: Field extraction, friendly type name formatting, `[NameOverride]` attribute handling
- **JsonSchemaGenerator**: Schema structure, JSON serialization, `$ref` usage, `$defs` alphabetical sorting, `id` and `isReferenceOnly` fields

## Usage

```txt
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

```txt
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

```txt
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
  "additionalProperties": false,
  "properties": {
    "componentType": {
      "const": "[FrooxEngine]FrooxEngine.AudioOutput"
    },
    "members": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "Enabled": { "$ref": "#/$defs/bool_value" },
        "persistent": { "$ref": "#/$defs/bool_value" },
        "UpdateOrder": { "$ref": "#/$defs/int_value" },
        "Volume": { "$ref": "#/$defs/float_value" },
        "Spatialize": { "$ref": "#/$defs/bool_value" },
        "Global": { "$ref": "#/$defs/nullable_bool_value" },
        "AudioTypeGroup": { "$ref": "#/$defs/AudioTypeGroup_value" }
      }
    },
    "id": { "type": "string" },
    "isReferenceOnly": { "type": "boolean" }
  },
  "required": ["id", "isReferenceOnly"],
  "$defs": {
    "bool_value": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "$type": { "const": "bool" },
        "value": { "type": "boolean" },
        "id": { "type": "string" }
      },
      "required": ["$type", "value", "id"]
    },
    "int_value": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "$type": { "const": "int" },
        "value": { "type": "integer" },
        "id": { "type": "string" }
      },
      "required": ["$type", "value", "id"]
    },
    "float_value": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "$type": { "const": "float" },
        "value": { "type": "number" },
        "id": { "type": "string" }
      },
      "required": ["$type", "value", "id"]
    },
    "nullable_bool_value": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "$type": { "const": "bool?" },
        "value": { "type": ["boolean", "null"] },
        "id": { "type": "string" }
      },
      "required": ["$type", "id"]
    },
    "AudioTypeGroup_value": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "$type": { "const": "string" },
        "value": {
          "type": "string",
          "enum": ["SoundEffect", "Multimedia", "Voice", "UI"]
        },
        "id": { "type": "string" }
      },
      "required": ["$type", "value", "id"]
    }
  }
}
```

### `ValueField<T>` Example (Generic Component)

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
      "additionalProperties": false,
      "properties": {
        "$type": { "const": "bool" },
        "value": { "type": "boolean" },
        "id": { "type": "string" }
      },
      "required": ["$type", "value", "id"]
    },
    "float3_value": {
      "type": "object",
      "additionalProperties": false,
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
        },
        "id": { "type": "string" }
      },
      "required": ["$type", "value", "id"]
    },
    "ValueField_bool": {
      "type": "object",
      "additionalProperties": false,
      "title": "ValueField<bool>",
      "properties": {
        "componentType": {
          "const": "[FrooxEngine]FrooxEngine.ValueField<bool>"
        },
        "members": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "Enabled": { "$ref": "#/$defs/bool_value" },
            "persistent": { "$ref": "#/$defs/bool_value" },
            "UpdateOrder": { "$ref": "#/$defs/int_value" },
            "Value": { "$ref": "#/$defs/bool_value" }
          }
        },
        "id": { "type": "string" },
        "isReferenceOnly": { "type": "boolean" }
      },
      "required": ["id", "isReferenceOnly"]
    },
    "ValueField_float3": {
      "type": "object",
      "additionalProperties": false,
      "title": "ValueField<float3>",
      "properties": {
        "componentType": {
          "const": "[FrooxEngine]FrooxEngine.ValueField<float3>"
        },
        "members": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "Enabled": { "$ref": "#/$defs/bool_value" },
            "persistent": { "$ref": "#/$defs/bool_value" },
            "UpdateOrder": { "$ref": "#/$defs/int_value" },
            "Value": { "$ref": "#/$defs/float3_value" }
          }
        },
        "id": { "type": "string" },
        "isReferenceOnly": { "type": "boolean" }
      },
      "required": ["id", "isReferenceOnly"]
    }
  }
}
```

## Type Mappings

All member values include a required `id` field (string) in addition to `$type` and `value`.

| FrooxEngine Type | ResoniteLink `$type` | Value Schema |
| ------------------ | --------------------- | -------------- |
| `Sync<Boolean>` | `"bool"` | `boolean` |
| `Sync<Boolean?>` | `"bool?"` | `["boolean", "null"]` (value optional) |
| `Sync<Int32>` | `"int"` | `integer` |
| `Sync<Single>` | `"float"` | `number` |
| `Sync<String>` | `"string"` | `["string", "null"]` |
| `Sync<colorX>` | `"colorX"` | `{r, g, b, a}` |
| `Sync<float3>` | `"float3"` | `{x, y, z}` |
| `Sync<floatQ>` | `"floatQ"` | `{x, y, z, w}` |
| `Sync<Enum>` | `"string"` | `string` with enum values |
| `SyncRef<T>` | `"reference"` | `{targetId, targetType}` |
| `AssetRef<T>` | `"reference"` | `{targetId, targetType}` |
| `FieldDrive<T>` | `"reference"` | `{targetId, targetType}` |
| `SyncList<T>` | `"syncList"` | `{elements: [...]}` |
| `SyncRefList<T>` | `"syncList"` | `{elements: [{reference}, ...]}` |

### Base Component Fields

All components inherit these fields from `ComponentBase`:

| Field Name | Type | Note |
| ------------ | ------ | ------ |
| `persistent` | `bool` | Lowercase (no NameOverride) |
| `UpdateOrder` | `int` | From `[NameOverride("UpdateOrder")]` |
| `Enabled` | `bool` | From `[NameOverride("Enabled")]` |

## License

MIT
