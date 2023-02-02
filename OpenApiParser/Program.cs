//using System;
//using System.Formats.Asn1;
//using System.Globalization;

using System.Data;
using System.Net;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;

namespace OpenApiParser;
internal class Program
{
    static void Main(string[] args)
    {
        SheetsService service = GetSheetService();

        var spreadsheetId = "1txAZ9ijlQbiwseeh87dOXfbEVT4DoNmb38hRON9PqaI";

        var ranges = new List<string>
        {
            "'GetReconciliationEvents '!A37:F59", //OrderPlaced
            "'GetReconciliationEvents '!A60:F64", //OrderRejected
            "'GetReconciliationEvents '!A66:F70", //OrderCancelled
            "'GetReconciliationEvents '!A72:F76", //OrderExpired
            "'GetReconciliationEvents '!A78:F100", //OrderExecuted
            "'GetReconciliationEvents '!A101:F110", //OrderModified
            "'GetReconciliationEvents '!G78:L100", //OrderExecutionCancelled
            "'GetReconciliationEvents '!M78:R100", //OrderExecutionRebookd
        };

        var schemas = ranges.Select(range =>
        {
            var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
            var response = request.Execute();
            var values = response.Values
                .Select(x => x.Select(y => (string)y).ToList()).ToList();

            var schema = Parse(values);

            return schema;

        }).ToArray();


        OpenApiDocument doc = CreateOpenApiDoc(schemas);
        using var sw = new StringWriter();
        //var tw = new TextWriter())
        var w = new OpenApiYamlWriter(sw);
        doc.SerializeAsV3(w);
        var openApiText = sw.ToString();
    }

    private static SheetsService GetSheetService()
    {
        // https://code-maze.com/google-sheets-api-with-net-core/
        // Credential: https://console.cloud.google.com/apis/credentials?project=excelparser-367605&supportedpurview=project
        // Service account: https://console.cloud.google.com/iam-admin/serviceaccounts/details/114154562945252957742;edit=true?project=excelparser-367605&supportedpurview=project

        GoogleCredential credential;
        using (var stream = new FileStream(@"C:\Users\woute\Downloads\excelparser-367605-603c41320f04.json", FileMode.Open, FileAccess.Read))
        {
            string[] Scopes = { SheetsService.Scope.Spreadsheets };
            credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
        }

        var service = new SheetsService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "OpenAPIParser"
        });

        var values = service.Spreadsheets.Values;
        return service;
    }

    private static (string, OpenApiSchema) Parse(List<List<string>> lines)
    {
        var state = State.TITLE_AND_DESCRIPTION;

        var endpoint = new Endpoint();
        Stack<IList<IProperty>> addTo = new();

        for (int i = 0; i < lines.Count; i++)
        {
            List<string>? line = lines[i];

            if (!line.Any())
                continue;

            if (state == State.TITLE_AND_DESCRIPTION)
            {
                endpoint = endpoint with
                {
                    Name = line[0],
                    Description = line[3]
                };
                state = State.DATA;
                addTo.Push(endpoint.Fields);
            }
            else if (state == State.DATA)
            {
                if (line[0].Contains("[]"))
                {
                    var (p0, _) = ParseFieldLine(line);

                    var prop = new ObjectProperty()
                    {
                        Field = p0.Field.Remove(line[0].Length - 2),
                        Description = p0.Description,
                        Required = p0.Required
                    };
                    addTo.Peek().Add(prop);

                    addTo.Push(prop.Properties);

                    continue;
                }

                var (p, e) = ParseFieldLine(line);

                addTo.Peek().Add(p);
                if (!string.IsNullOrWhiteSpace(e))
                {
                    if (state == State.DATA)
                        endpoint = endpoint with { Example = e };
                }


                // Look ahead if this is the end
                if (!lines.HasIndex(i + 1) || (lines.HasIndex(i + 1) && !lines[i + 1].Any()))
                {
                    addTo.Pop();
                }
            }
            
        }

        return CreateOpenApiSchema(endpoint);
    }

    private static (Property p, string? InputExample) ParseFieldLine(List<string> line)
    {
        var prop = new Property();
        List<string> descSuffix = new();

        // Field
        prop = prop with { Field = line.ElementAtOrDefault(0) };

        // Type
        string type = line.ElementAtOrDefault(1);
        if (type == "enum" || type == "enumerated")
        {
            prop = prop with { Type = "string" };

            var enumValues = line.ElementAtOrDefault(3);
            if (enumValues.Contains("Possible enum values:"))
            {
                enumValues = enumValues.Substring(enumValues.IndexOf("Possible enum values:"));
                enumValues = enumValues.Replace("Possible enum values:", "");
            }
            else if (enumValues.Contains("Can be one of:"))
            {
                enumValues = enumValues.Substring(enumValues.IndexOf("Can be one of:"));
                enumValues = enumValues.Replace("Can be one of:", "");
            }
            //else 
                //if (enumValues == "The ISO 4217 currency code of the instrument currency.")
                // ...

            enumValues = enumValues.TrimEnd('.');
            enumValues = enumValues.Trim();
            enumValues = enumValues.Replace(" or ", ",");
            enumValues = enumValues.Replace(", ", ",");
            prop = prop with { EnumValues = enumValues.Split(',') };
        }
        else if (type == "decimal" || type == "timestamp")
        {
            prop = prop with { Type = "number" };
            descSuffix.Add($"Type: {type}");
        }
        else if (type == "date")
        {
            prop = prop with { Type = "string", Format = "date" };
        }
        else
            prop = prop with { Type = type };

        // Required
        string req = line.ElementAtOrDefault(2);
        if (req == "always" || req == "required")
            prop = prop with { Required = true };
        else if (req == "optional")
            prop = prop with { Required = false };
        else
        {
            prop = prop with { Required = false };
            descSuffix.Add(req);
        }

        // Description
        if (descSuffix.Any())
            prop = prop with { Description = line.ElementAtOrDefault(3) + '\n' + String.Join('\n', descSuffix) };
        else
            prop = prop with { Description = line.ElementAtOrDefault(3) };

        // JSON Example
        string? example = default;
        if (line.HasIndex(5))
            if (line[5].Contains('{'))
                example = line.ElementAtOrDefault(5);

        return (prop, example);
    }


    enum State
    {
        TITLE_AND_DESCRIPTION,
        DATA
    }
    enum SubState
    {
        NORMAL,
        ARRAY
    }

    private static OpenApiDocument CreateOpenApiDoc((string, OpenApiSchema)[] schemas)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Version = "1.1.0",
                Title = "InvestSuite Broker/Custodian Agnostic API",
                Contact = new OpenApiContact { Email = "info@investsuite.com" },
                Description = "InvestSuite Broker/Custodian Agnostic API"
            },
            Components = new OpenApiComponents
            {
                Schemas = schemas.ToDictionary(s => s.Item1, s => s.Item2)
            }
        };
    }

    private static (string, OpenApiSchema) CreateOpenApiSchema(Endpoint endpoint)
    {
        var s = new OpenApiSchema
        {
            Type = "object",
            Description = endpoint.Description,
            Properties = endpoint.Fields.ToDictionary(e => e.Field, e => GetOpenApiProperty(e)),
            Required = endpoint.Fields.Where(e => e.Required ?? false).Select(e => e.Field).ToHashSet(),
            Example = GetExample(endpoint.Example)
            //    new OpenApiArray
            //{
            //    new OpenApiObject
            //    {

            //    }

            //        new OpenApiExample
            //        {
            //            Value = new OpenApiString(endpoint.InputExample)
            //        }
            //}
        };

        return (endpoint.Name, s);
    }

    private static IOpenApiAny GetExample(string? jsonString)
    {
        if (jsonString == null)
            return null;
        
        var json = System.Text.Json.JsonSerializer.Deserialize<dynamic>(jsonString);

        return new OpenApiString(jsonString);
    }

    static OpenApiSchema GetOpenApiProperty(IProperty p) => GetOpenApiProperty((dynamic)p);
    static OpenApiSchema GetOpenApiProperty(Property p)
    {
        return new OpenApiSchema()
        {
            Type = p.Type,
            Format = p.Format,
            Description = p.Description,
            Enum = p.EnumValues?.Select(e => new OpenApiString(e)).ToArray()
        };
    }
    static OpenApiSchema GetOpenApiProperty(ObjectProperty p)
    {
        return new OpenApiSchema()
        {
            Type = "object",
            Properties = p.Properties.ToDictionary(e => e.Field, e => GetOpenApiProperty(e))
        };
    }


    record Endpoint(string Name, string? Description, List<IProperty> Fields, string? Example, List<IProperty> Output, string? OutputExample)
    {
        public Endpoint() : this(null, null, new(), null, new(), null) 
        { 
        }
    }

    interface IProperty
    {
        public string? Field { get; }
        public bool? Required { get; }
        public string? Type { get; }
        public string? Description { get; }
    }
    record ObjectProperty(string? Field, bool? Required, string? Description, List<IProperty> Properties) : IProperty
    {
        public ObjectProperty() : this(null, null, null, new())
        {
        }
        
        string? IProperty.Type => "object";
    }
    record Property(string? Field = null, string? Type = null, string? Format = null, string[]? EnumValues = null, bool? Required = null, string? Description = null) : IProperty;
}

public static class Extensions
{
    public static bool HasIndex<T>(this IEnumerable<T> x, int index)
    {
        return x.Count() > index;
    }
}