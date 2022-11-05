//using System;
//using System.Formats.Asn1;
//using System.Globalization;

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

        var request = service.Spreadsheets.Values.Get(spreadsheetId, "GetCurrentOrders");
        var response = request.Execute();
        var values = response.Values
            .Select(x => x.Select(y => (string)y).ToList()).ToList();

        Parse(values);
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

    private static void Parse(List<List<string>> lines)
    {
        var state = State.TITLE;

        var endpoint = new Endpoint();
        Stack<IList<IProperty>> addTo = new();

        for (int i = 0; i < lines.Count; i++)
        {
            List<string>? line = lines[i];

            if (!line.Any())
                continue;

            if (state == State.TITLE)
            {
                endpoint = endpoint with { Name = line.Single() };
                state = State.DESCRIPTION;
            }
            else if (state == State.DESCRIPTION)
            {
                endpoint = endpoint with { Description = line.Single() };
                state = State.INPUT_DATA_SEEK;
            }
            else if (state == State.INPUT_DATA_SEEK || state == State.OUTPUT_DATA_SEEK)
            {
                if (state == State.INPUT_DATA_SEEK && line.First() == "Provided input data")
                    state = State.INPUT_DATA_HEADER;
                else if (state == State.OUTPUT_DATA_SEEK && line.First() == "Requested output data")
                    state = State.OUTPUT_DATA_HEADER;
                else
                    throw new Exception();
            }
            else if (state == State.INPUT_DATA_HEADER || state == State.OUTPUT_DATA_HEADER)
            {
                if (state == State.INPUT_DATA_HEADER && line.First() == "field")
                {
                    state = State.INPUT_DATA;
                    addTo.Push(endpoint.Input);
                }
                else if (state == State.OUTPUT_DATA_HEADER && line.First() == "field")
                {
                    state = State.OUTPUT_DATA;
                    addTo.Push(endpoint.Output);
                }
                else
                    throw new Exception();
            }
            else if (state == State.INPUT_DATA || state == State.OUTPUT_DATA)
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
                    if (state == State.INPUT_DATA)
                        endpoint = endpoint with { InputExample = e };
                    else if (state == State.OUTPUT_DATA)
                        endpoint = endpoint with { OutputExample = e };
                }


                // Look ahead if this is the end
                if (!lines.HasIndex(i + 1) || (lines.HasIndex(i + 1) && !lines[i + 1].Any()))
                {
                    addTo.Pop();

                    if (state == State.INPUT_DATA)
                        state = State.OUTPUT_DATA_SEEK;
                }
            }
            
        }

        OpenApiDocument doc = CreateOpenApiDoc(endpoint);
        using var sw = new StringWriter();
        //var tw = new TextWriter())
        var w = new OpenApiYamlWriter(sw);
        doc.SerializeAsV3(w);
        var openApiText = sw.ToString();
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
        if (line.HasIndex(4))
            if (line[4].Contains('{'))
                example = line.ElementAtOrDefault(4);

        return (prop, example);
    }


    enum State
    {
        TITLE,
        DESCRIPTION,
        INPUT_DATA_SEEK,
        INPUT_DATA_HEADER,
        INPUT_DATA,
        OUTPUT_DATA_SEEK,
        OUTPUT_DATA_HEADER,
        OUTPUT_DATA
    }
    enum SubState
    {
        NORMAL,
        ARRAY
    }

    private static OpenApiDocument CreateOpenApiDoc(Endpoint endpoint)
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
            Paths = new OpenApiPaths
            {
                [endpoint.Name] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            Description = endpoint.Description,
                            RequestBody = new OpenApiRequestBody
                            {
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    ["application/json"] = new OpenApiMediaType
                                    {
                                        Schema = new OpenApiSchema
                                        {
                                            Type = "object",
                                            Properties = endpoint.Input.ToDictionary(e => e.Field, e => GetOpenApiProperty(e))
                                        },
                                        Examples = new Dictionary<string, OpenApiExample>
                                        {
                                            ["example"] = new OpenApiExample
                                            {
                                                Value = new OpenApiString(endpoint.InputExample)
                                            }
                                        }

                                    }
                                }
                            },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Content = new Dictionary<string, OpenApiMediaType>
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            Schema = new OpenApiSchema
                                            {
                                                Type = "object",
                                                Properties = endpoint.Output.ToDictionary(e => e.Field, e => GetOpenApiProperty(e))
                                            },
                                            Examples = new Dictionary<string, OpenApiExample>
                                            {
                                                ["example"] = new OpenApiExample
                                                {
                                                    Value = new OpenApiString(endpoint.OutputExample)
                                                }
                                            }

                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    static OpenApiSchema GetOpenApiProperty(IProperty p) => GetOpenApiProperty((dynamic)p);
    static OpenApiSchema GetOpenApiProperty(Property p)
    {
        return new OpenApiSchema()
        {
            Type = p.Type,
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


    record Endpoint(string? Name, string? Description, List<IProperty> Input, string? InputExample, List<IProperty> Output, string? OutputExample)
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
    record Property(string? Field = null, string? Type = null, string[]? EnumValues = null, bool? Required = null, string? Description = null) : IProperty;
}

public static class Extensions
{
    public static bool HasIndex<T>(this IEnumerable<T> x, int index)
    {
        return x.Count() > index;
    }
}