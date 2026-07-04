using System.Text.Json;
using System.Text.Json.Nodes;
using WaPlatform.Contracts.TemplateDsl;

namespace WaAdmin.Application.Templates;

/// <summary>
/// Compiles the platform-native <see cref="TemplateDefinition"/> DSL (WaPlatform.Contracts) into
/// the Meta Graph API "components" JSON array, per the DSL's own doc comment ("wa-admin-svc
/// compiles this to the Graph API template payload on submit", issue #16). Deliberately dumb: it
/// is a structural mapping only, not a validator — <see cref="Common.Interfaces.ITemplateLintService"/>
/// is the (stub, Wave 1) place policy/shape validation belongs.
/// </summary>
public static class TemplateDefinitionCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Meta component JSON: <c>[{ "type": "BODY", "text": "..." }, ...]</c>, with any
    /// component-specific extras (button definitions, header media format, …) merged in from
    /// <see cref="TemplateComponent.ExtrasJson"/> when present.</summary>
    public static string CompileComponents(TemplateDefinition definition)
    {
        var array = new JsonArray();

        foreach (var component in definition.Components)
        {
            var node = new JsonObject
            {
                ["type"] = component.Type.ToUpperInvariant(),
            };

            if (component.Text is not null)
                node["text"] = component.Text;

            if (component.ExtrasJson is not null)
            {
                var extras = JsonNode.Parse(component.ExtrasJson) as JsonObject
                    ?? throw new FormatException("Component ExtrasJson must be a JSON object.");
                foreach (var (key, value) in extras)
                    node[key] = value?.DeepClone();
            }

            array.Add(node);
        }

        return array.ToJsonString(JsonOptions);
    }

    /// <summary>Meta category string (marketing | utility | authentication) from the DSL enum.</summary>
    public static string CompileCategory(TemplateCategory category) => category switch
    {
        TemplateCategory.Marketing => "marketing",
        TemplateCategory.Utility => "utility",
        TemplateCategory.Authentication => "authentication",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown template category."),
    };
}
