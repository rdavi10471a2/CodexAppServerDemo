using System.Text.Json.Nodes;
using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Tests;

public sealed class ElicitationRequestSchemaParserTests
{
    [Fact]
    public void Parse_reads_boolean_form_field()
    {
        ElicitationRequestSchema? schema = ElicitationRequestSchemaParser.Parse(
            """
            {
              "id": 10,
              "method": "mcpServer/elicitation/request",
              "params": {
                "serverName": "harness",
                "threadId": "thread-1",
                "mode": "form",
                "message": "Should I update agent notes?",
                "requestedSchema": {
                  "type": "object",
                  "required": ["updateNotes"],
                  "properties": {
                    "updateNotes": {
                      "type": "boolean",
                      "title": "Update agent notes",
                      "description": "Choose yes only if durable workflow memory changed.",
                      "default": false
                    }
                  }
                }
              }
            }
            """);

        Assert.NotNull(schema);
        Assert.Equal("form", schema.Mode);
        ElicitationFieldDefinition field = Assert.Single(schema.Fields);
        Assert.Equal("updateNotes", field.Name);
        Assert.Equal(ElicitationFieldKind.Boolean, field.Kind);
        Assert.True(field.IsRequired);
        Assert.False(field.DefaultBoolean);
        Assert.Equal("Update agent notes", field.Label);
    }

    [Fact]
    public void Parse_reads_enum_field_options()
    {
        ElicitationRequestSchema? schema = ElicitationRequestSchemaParser.Parse(
            """
            {
              "params": {
                "mode": "form",
                "requestedSchema": {
                  "type": "object",
                  "properties": {
                    "decision": {
                      "type": "string",
                      "title": "Decision",
                      "enum": ["yes", "no"],
                      "enumNames": ["Yes", "No"],
                      "default": "no"
                    }
                  }
                }
              }
            }
            """);

        Assert.NotNull(schema);
        ElicitationFieldDefinition field = Assert.Single(schema.Fields);
        Assert.Equal(ElicitationFieldKind.Enum, field.Kind);
        Assert.Equal("no", field.DefaultText);
        Assert.Collection(
            field.Options,
            option =>
            {
                Assert.Equal("yes", option.Value);
                Assert.Equal("Yes", option.Label);
            },
            option =>
            {
                Assert.Equal("no", option.Value);
                Assert.Equal("No", option.Label);
            });
    }
}
