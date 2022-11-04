//using System;
//using System.Formats.Asn1;
//using System.Globalization;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

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

        Parse2(values);

    }

    private static SheetsService GetSheetService()
    {
        // https://code-maze.com/google-sheets-api-with-net-core/

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


    private static void Parse2(List<List<string>> lines)
    {
        var state = State.TITLE;

        var endpoint = new Endpoint();

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
            else if (state == State.INPUT_DATA_SEEK)
            {
                if (line.First() == "Provided input data")
                    state = State.INPUT_DATA_HEADER;
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
            else if (state == State.INPUT_DATA)
            {
                var prop = new Property();

                // Field
                prop = prop with { Field = line[0] };

                // Type
                prop = prop with { Type = line[1] };

                // Required
                if (line[2] == "always" || line[2] == "required")
                    prop = prop with { Required = true };
                else if (line[2] == "optional")
                    prop = prop with { Required = false };
                else
                    throw new Exception();

                // Description
                prop = prop with { Description = line[3] };

                endpoint.Input.Add(prop);

                // JSON Example
                if (line.HasIndex(4))
                    if (line[4].Contains('{'))
                        endpoint = endpoint with { InputExample = line[4] };

                // Look ahead if this is the end
                if (!lines[i + 1].Any())
                    endpoint = endpoint with { Input = }


            }
        }
    }

   

    enum State
    {
        TITLE,
        DESCRIPTION,
        INPUT_DATA_SEEK,
        INPUT_DATA_HEADER,
        INPUT_DATA,
    }

    //    private static File[] GetFiles()
    //    {
    //        return new File[]
    //        {
    //            new File(@"C:\Users\woute\Downloads\InvestSuite - broker_custodian integration requirements  - GetPendingOrders.csv")
    //        };
    //    }

    //    record File(string Path);
    record Endpoint(string? Name, string? Description, List<Property> Input, string? InputExample, List<Property> Output, string? OutputExample)
    {
        public Endpoint() : this(null, null, new(), null, new(), null) 
        { 
        }
    }

    record Array(Property[] Proprerties);
    record Property(string? Field = null, string? Type = null, bool? Required = null, string? Description = null);

    
}

public static class Extensions
{
    public static bool HasIndex<T>(this IEnumerable<T> x, int index)
    {
        return x.Count() > index;
    }
}