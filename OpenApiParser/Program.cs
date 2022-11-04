using System;
using System.Formats.Asn1;
using System.Globalization;

namespace OpenApiParser;
internal class Program
{
    static void Main(string[] args)
    {
        var files = GetFiles();
        foreach (var file in files)
        {
            var state = State.TITLE;

            using var reader = new StreamReader(file.Path);

            string title;
            string description;

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine().Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                
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
    }

    enum State
    {
        TITLE,
        DESCRIPTION,
        INPUT_DATA_SEEK,
        INPUT_DATA_HEADER,
        INPUT_DATA,
    }

    private static File[] GetFiles()
    {
        return new File[]
        {
            new File(@"C:\Users\woute\Downloads\InvestSuite - broker_custodian integration requirements  - GetPendingOrders.csv")
        };
    }

    record File(string Path);
    record Parse(string Title, string Description);

    record Array(Property[] Proprerties);
    record Property(string Field, string Type, bool Required, string Description);
        



}