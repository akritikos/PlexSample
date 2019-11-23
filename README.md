# Assigment

In this [assigment][assign], we're expected to parse the contents of a provided [input file][data] that has the following format:

    \# {Name} [{Description}]
    \# area (lat: {MinLat}, {MaxLat}) - (lon: {MinLon}, {MaxLon}) [...]
    \# {DeprecatedOrDiscontinued}
    <{Identity}> {ProjValue} <>

The brackets represent the wanted data that should be parsed and transformed to a column output format with each line representing a single record. ```DeprecatedOrDiscontinued``` should be empty if not present in the input file.

## Solution Workflow

Given the multi-record format of the file, we have two options to parse said data. We either go for a heavily configured parsing library (such as FileHelpers) which will require string testing to ensure the correct data is being read, or fall back to a line by line parsing with Regular Expressions in a very basic automaton. We could also create an LL(*) parser but this is deemed out of scope for this specific analysis.

The rest of the documentation continues down the Regular Expression path due to its simplicity and lack of dependencies on external libraries. Apart from Roslyn Analyzers loaded by configuration files to enforce code consistency and help avoid common pitfalls, the only external library we're going to use is Serilog, to simultaneously make our console output a bit more readable and export data in the requested format (albeit without headers).

### Requirements

- [.NET Core 3.0 SDK][netcore3]

Steps to solve:

- Create a POCO that can hold the needed data ([GeoModel.cs][poco])
- Break each record into lines for simplicity and try to parse each one alone with a RegEx
- Define said RegEx in static fields, using the ```RegexOptions.Compiled``` directive for performance.
- (Optional) Configure Serilog output for both console and needed file. If not, figure out how you want to save parsed data.
- Check if input file exists, and open it up for reading line by line following best practices

We have now arrived at the main point of this exercise, the handling of read data. While one could use a typical switch block based on the number of lines we have parsed (or since the last empty line) to figure out which regex should be applied, this scenario is a prime candidate for a feature introduced in C# 8.0: switch expressions with pattern matching.
To elaborate, we can apply our RegEx definitions in order to the line read and act only when we find a match, or an empty line is encountered.

In case we found a RegEx match, we're ready to extract data from the match groups into our current record. **Care is needed** on the order of RegEx application, in order to keep the sample solution simple, the pattern for the first row can also capture the second (but not vice-versa). So we start by testing if the line we read is the second row of a record, and fall through to the rest as needed. Data extracting is done in private extension methods in order to simplify the main code.

Therefore, the order of parsing is as follows:

- Try to match 2nd row pattern
- Try to match 1st row pattern
- Try to match 3rd row pattern
- Try to match 4th row pattern
- If the line is empty, save current record to the list of read records and restart the process with a new record
- Current line does not conform to known patterns, throw exception to notify the user

Following a successful run, output.txt is produced next to the binary of PlexSample.Runner containing all read rows in a tab delimited format without headers for easier parsing.

[assign]: Assigment.txt
[data]: input.txt
[poco]: src/GeoData.Model/GeoModel.cs
[netcore3]: https://dotnet.microsoft.com/download/dotnet-core/3.0
