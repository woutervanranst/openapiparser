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

        string title;
        string description;

        foreach (var line in lines)
        {
            if (!line.Any())
                continue;

            if (state == State.TITLE)
            {
                title = line.Single();
                state = State.DESCRIPTION;
            }
            else if (state == State.DESCRIPTION)
            {
                description = line.Single();
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
                // Field
                var field = line[0];

                // Type
                var type = line[1];

                // Required
                bool required;
                if (line[2] == "always" || line[2] == "required")
                    required = true;
                else if (line[2] == "optional")
                    required = false;
                else
                    throw new Exception();

                // Description
                var descr = line[3];

                // JSON Example
                var example = line[4];

                //var prop = new Property(line[0], )

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
    record Parse(string Title, string Description);

    record Array(Property[] Proprerties);
    record Property(string Field, string Type, bool Required, string Description);

}