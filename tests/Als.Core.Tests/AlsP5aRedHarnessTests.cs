using System.Reflection;
using System.Text.Json.Nodes;

namespace GodotAls.Core.Tests;

public sealed class AlsP5aRedHarnessTests
{
    [Theory]
    [InlineData("native_raw", "/nativeReferenceAudit/events", true)]
    [InlineData("native_raw", "/nativeReferenceAudit/auxiliaryAssets/4/events", true)]
    [InlineData("native_raw", "/cases/0/frames/0/nativeActual/canonicalAssetOracle/events", false)]
    [InlineData("native_canonical", "/cases/0/frames/0/comparableActual/events", false)]
    [InlineData("port_canonical", "/cases/0/frames/0/portAudit/result/events", false)]
    [InlineData("port_canonical", "/cases/0/frames/0/portAudit/stateAfter/timelineCursors", true)]
    [InlineData("port_canonical", "/cases/0/frames/0/portAudit/prepared/syncMappings", false)]
    [InlineData("plan", "/cases/0/frames/0/input/p4Curves/base", true)]
    public void ClosedShapeArrayPolicyUsesRepresentationAndFullPath(
        string representation,
        string path,
        bool expected)
    {
        var method = typeof(P5aRedHarness).GetMethod(
            "IsLockedArrayForClosedShape",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method!.Invoke(null, [representation, path])!);
    }

    [Fact]
    public void ObjectSchemaLinterAllowsRefinementOfAClosedBase()
    {
        var schema = JsonNode.Parse("""
            {
              "$defs": {
                "base": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["id"],
                  "properties": {
                    "id": { "type": "string" }
                  }
                },
                "specialized": {
                  "allOf": [
                    { "$ref": "#/$defs/base" },
                    { "properties": { "id": { "const": "approved" } } }
                  ]
                }
              }
            }
            """)!.AsObject();

        P5aRedHarness.AssertEveryObjectSchemaIsClosed(schema);
    }

    [Fact]
    public void ObjectSchemaLinterRejectsUnknownPropertyInRefinement()
    {
        var schema = JsonNode.Parse("""
            {
              "$defs": {
                "base": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["id"],
                  "properties": {
                    "id": { "type": "string" }
                  }
                },
                "specialized": {
                  "allOf": [
                    { "$ref": "#/$defs/base" },
                    { "properties": { "unexpected": { "const": 1 } } }
                  ]
                }
              }
            }
            """)!.AsObject();

        Assert.ThrowsAny<Exception>(() => P5aRedHarness.AssertEveryObjectSchemaIsClosed(schema));
    }

    [Fact]
    public void ObjectSchemaLinterRejectsObjectWithoutClosedShape()
    {
        var schema = JsonNode.Parse("""
            {
              "$defs": {
                "open": {
                  "type": "object",
                  "properties": {
                    "id": { "type": "string" }
                  }
                }
              }
            }
            """)!.AsObject();

        Assert.ThrowsAny<Exception>(() => P5aRedHarness.AssertEveryObjectSchemaIsClosed(schema));
    }

    [Fact]
    public void ObjectSchemaLinterRejectsObjectWithoutProperties()
    {
        var schema = JsonNode.Parse("""
            {
              "$defs": {
                "closed": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["id"],
                  "properties": {
                    "id": { "type": "string" }
                  }
                },
                "open": {
                  "type": "object",
                  "additionalProperties": false
                }
              }
            }
            """)!.AsObject();

        Assert.ThrowsAny<Exception>(() => P5aRedHarness.AssertEveryObjectSchemaIsClosed(schema));
    }

    [Fact]
    public void ObjectSchemaLinterRejectsObjectWithIncompleteRequiredSet()
    {
        var schema = JsonNode.Parse("""
            {
              "$defs": {
                "incomplete": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["id"],
                  "properties": {
                    "id": { "type": "string" },
                    "version": { "type": "integer" }
                  }
                }
              }
            }
            """)!.AsObject();

        Assert.ThrowsAny<Exception>(() => P5aRedHarness.AssertEveryObjectSchemaIsClosed(schema));
    }

    [Fact]
    public void ObjectSchemaLinterRejectsPropertiesFragmentWithoutClosedBase()
    {
        var schema = JsonNode.Parse("""
            {
              "$defs": {
                "orphan": {
                  "allOf": [
                    { "properties": { "id": { "const": "approved" } } }
                  ]
                }
              }
            }
            """)!.AsObject();

        Assert.ThrowsAny<Exception>(() => P5aRedHarness.AssertEveryObjectSchemaIsClosed(schema));
    }
}
