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

        var request = service.Spreadsheets.Values.Get(spreadsheetId, "GetPendingOrders");
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
        List<IProperty> addTo;

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
                    state = State.OUTPUT_DATA;
                else
                    throw new Exception();
            }
            else if (state == State.INPUT_DATA_HEADER)
            {
                if (line.First() == "field")
                    state = State.INPUT_DATA;
                else
                    throw new Exception();
            }
            else if (state == State.INPUT_DATA || state == State.OUTPUT_DATA)
            {
                addTo = state switch
                {
                    State.INPUT_DATA => endpoint.Input,
                    State.OUTPUT_DATA => endpoint.Output
                };

                if (line[0].Contains("[]"))
                {
                    continue;
                }

                var (p, e) = ParseFieldLine(line);

                addTo.Add(p);
                if (!string.IsNullOrWhiteSpace(e))
                {
                    if (state == State.INPUT_DATA)
                        endpoint = endpoint with { InputExample = e };
                    else if (state == State.OUTPUT_DATA)
                        endpoint = endpoint with { OutputExample = e };
                }


                // Look ahead if this is the end
                if (lines.HasIndex(i + 1) && !lines[i + 1].Any())
                    state = State.OUTPUT_DATA_SEEK;
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

        // Field
        prop = prop with { Field = line[0] };

        // Type
        prop = prop with { Type = line[1] };

        // Required
        string? descSuffix = default;
        if (line[2] == "always" || line[2] == "required")
            prop = prop with { Required = true };
        else if (line[2] == "optional")
            prop = prop with { Required = false };
        else
        {
            prop = prop with { Required = false };
            descSuffix = line[2];
        }

        // Description
        if (string.IsNullOrEmpty(descSuffix))
            prop = prop with { Description = line[3] };
        else
            prop = prop with { Description = line[3] + '\n' + descSuffix };

        // JSON Example
        string? example = default;
        if (line.HasIndex(4))
            if (line[4].Contains('{'))
                example = line[4];

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
                ["/PendingOrders"] = new OpenApiPathItem
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
                                            Properties = endpoint.Input.OfType<Property>().ToDictionary(e => e.Field, e => new OpenApiSchema()
                                            {
                                                Type = e.Type,
                                                Description = e.Description
                                            })
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
                                                Properties = endpoint.Output.OfType<Property>().ToDictionary(e => e.Field, e => new OpenApiSchema()
                                                {
                                                    Type = e.Type,
                                                    Description = e.Description
                                                })
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


    //    private static File[] GetFiles()
    //    {
    //        return new File[]
    //        {
    //            new File(@"C:\Users\woute\Downloads\InvestSuite - broker_custodian integration requirements  - GetPendingOrders.csv")
    //        };
    //    }

    //    record File(string Path);
    record Endpoint(string? Name, string? Description, List<IProperty> Input, string? InputExample, List<IProperty> Output, string? OutputExample)
    {
        public Endpoint() : this(null, null, new(), null, new(), null) 
        { 
        }
    }

    interface IProperty { }
    record Array(Property Parent, Property[] Proprerties) : IProperty;
    record Property(string? Field = null, string? Type = null, bool? Required = null, string? Description = null) : IProperty;

    
}

public static class Extensions
{
    public static bool HasIndex<T>(this IEnumerable<T> x, int index)
    {
        return x.Count() > index;
    }
}