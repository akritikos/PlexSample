namespace Kritikos.GeoData.Runner
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.CodeAnalysis;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Text.RegularExpressions;

	using Kritikos.GeoData.Model;

	using Serilog;
	using Serilog.Sinks.SystemConsole.Themes;

	public static class Program
	{
		/// <summary>
		/// Structured Log Template for output.
		/// </summary>
		private const string GeoDataLogTemplate =
			"{Identity}\t{Name}\t{Description}\t{MinLat}\t{MaxLat}\t{MinLon}\t{MaxLon}\t{ProjValue}\t{DeprecatedOrDiscontinued}";

		/// <summary>
		/// Extremely basic serilog configuration for output and debugging.
		/// </summary>
		private static readonly LoggerConfiguration BasicLoggerConfiguration = new LoggerConfiguration()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.WriteTo.File("output.txt", encoding: Encoding.UTF8, outputTemplate: "{Message:lj}{NewLine}", shared: true);

		/// <summary>
		/// Correlates line types with their respective regex patterns
		/// </summary>
		private static readonly (GeoLineType type, Regex pattern)[] Patterns =
		{
			(GeoLineType.Location, new Regex(
				@"\(lat: (?<MinLat>(?:\-*)\d+.\d+), (?<MaxLat>(?:\-*)\d+.\d+)\) - \(lon: (?<MinLon>(?:\-*)\d+.\d+), (?<MaxLon>(?:\-*)\d+.\d+)",
				RegexOptions.Compiled)),

			// Pattern of the first row in each record. **CAN ALSO CAPTURE FIRST LINE, CARE WITH ORDER**
			(GeoLineType.Header, new Regex(
				@"# (?<Name>.+?) \[(?<Description>.+?)\]",
				RegexOptions.Compiled)),

			// Pattern of the second row in each record.
			(GeoLineType.Obsoletion, new Regex(
				@"# (?<Obsolete>DISCONTINUED|DEPRECATED)",
				RegexOptions.Compiled)),

			// Pattern of the third row in each record.
			(GeoLineType.Identity, new Regex(
				@"<(?<Identity>\d+)> \+proj=(?<ProjValue>.+?) ",
				RegexOptions.Compiled)),

			// Empty lines between records
			(GeoLineType.Empty, new Regex(
				@"^$",
				RegexOptions.Compiled)),
		};

		/// <summary>
		/// Simple collection in case the file output by Serilog isn't enough.
		/// </summary>
		private static readonly List<GeoModel> Data = new List<GeoModel>();

		[SuppressMessage(
			"Exceptions usages",
			"EX006:Do not write logic driven by exceptions.",
			Justification = "Console App, recovering from this error is not in the scope of the assigment.")]
		[SuppressMessage(
			"Design",
			"CA1031:Do not catch general exception types",
			Justification = "Final frontier for log output, can't catch more specific exceptions")]
		public static void Main(string[] args)
		{
			var path = args?.Length > 0
				? args[0]
				: "input.txt";

			// Clears output of previous runs
			var output = new FileInfo("output.txt");
			output.Delete();

			Log.Logger = BasicLoggerConfiguration.CreateLogger();

			try
			{
				// Checks if input file exists in expected path
				var file = new FileInfo(path);
				if (!file.Exists)
				{
					throw new FileNotFoundException("File not found!", file.FullName);
				}

				// Opens a disposable stream (C# 8.0 feature) to read the file
				using var stream = new StreamReader(file.FullName);

				// Initalization for the first iteration
				string? line;
				var data = new GeoModel();

				// For each line in input file, in the order encountered
				while ((line = stream.ReadLine()) != null)
				{
					var patternMatch = Patterns
						.Select(t => (LineType: t.type, Match: t.pattern.Match(line)))
						.FirstOrDefault(t => t.Match.Success);

					// Figure out if this line matches the regex of a specific row and act accordingly
					// Top down evaluation, the GeoDataHeader pattern always matches both the first and second rows
					// so we evaluate GeoDataLocation first to capture the second row if applicable
					data = patternMatch switch
					{
						// An empty line -> Save currently parsed record to the list, output to console/file and return
						//                  a new record to continue the process
						(GeoLineType.Empty, _) => data.PersistGeoData(),

						// The second row of a record -> Populate Lat/Long fields from values discovered
						(GeoLineType.Location, var match) => data.ParseGeoDataLocation(match),

						// The first row of a record -> Populate Name and Description values
						(GeoLineType.Header, var match) => data.ParseGeoDataHeader(match),

						// The third (optional) line of a record -> Populate field describing obsoletion status
						(GeoLineType.Obsoletion, var match) => data.ParseGeoDataObsoletion(match),

						// The fourth (final) line of a record -> Populate Identity and Project Value fields
						(GeoLineType.Identity, var match) => data.ParseGeoDataIdentity(match),

						// A line not matching any of the previous patterns, we don't know what we just read
						_ => throw new InvalidDataException($"Line is in unexpected format: {line}"),
					};
				}
			}

			// Explicit generic exception catch, despite the bad practice it represents, this would be our
			// final frontier in saving currently read data or handling an exception
			catch (Exception e)
			{
				Log.Fatal(e, "Unhandled exception! {Message}", e.Message);
			}

			// Serilog uses buffers to avoid slowing down the program, we must make sure all data is flushed
			// to disk or network sinks before exiting
			finally
			{
				Log.CloseAndFlush();
			}
		}

		/// <summary>
		/// Saves currently loaded record in list of results, (logs to output if configured)
		/// and returns a new record instance to parse the remaining records.
		/// </summary>
		/// <param name="data"><see cref="GeoModel"/> to operate on.</param>
		/// <returns>A new <see cref="GeoModel"/> instance.</returns>
		private static GeoModel PersistGeoData(this GeoModel data)
		{
			Data.Add(data);
			Log.Information(
				GeoDataLogTemplate,
				data.Identity,
				data.Name,
				data.Description,
				data.MinimumLatitude,
				data.MaximumLatitude,
				data.MinimumLongitude,
				data.MaximumLongitude,
				data.ProjectValue,
				data.ObsoleteDescription);
			return new GeoModel();
		}

		/// <summary>
		/// Parses Name and Description named using named groups from a successful match
		/// and updates the corresponding fields of the <paramref name="data"/> parameter.
		/// </summary>
		/// <param name="data"><see cref="GeoModel"/> to operate on.</param>
		/// <param name="match">Successful Regex Match.</param>
		/// <returns><paramref name="data"/> parameter updated with the parsed values.</returns>
		private static GeoModel ParseGeoDataHeader(this GeoModel data, Match match)
		{
			data.Name = match.Groups["Name"].Value;
			data.Description = match.Groups["Description"].Value;
			return data;
		}

		/// <summary>
		/// Parses lattitude and longitude using named groups from a successful match
		/// and updates the corresponding fields of the <paramref name="data"/> parameter.
		/// </summary>
		/// <param name="data"><see cref="GeoModel"/> to operate on.</param>
		/// <param name="match">Successful Regex Match.</param>
		/// <returns><paramref name="data"/> parameter updated with the parsed values.</returns>
		private static GeoModel ParseGeoDataLocation(this GeoModel data, Match match)
		{
			data.MinimumLatitude = double.Parse(match.Groups["MinLat"].Value, CultureInfo.InvariantCulture);
			data.MaximumLatitude = double.Parse(match.Groups["MaxLat"].Value, CultureInfo.InvariantCulture);
			data.MinimumLongitude = double.Parse(match.Groups["MinLon"].Value, CultureInfo.InvariantCulture);
			data.MaximumLongitude = double.Parse(match.Groups["MaxLon"].Value, CultureInfo.InvariantCulture);
			return data;
		}

		/// <summary>
		/// Parses Obsoletion status using named groups from a successful match
		/// and updates the corresponding fields of the <paramref name="data"/> parameter.
		/// </summary>
		/// <param name="data"><see cref="GeoModel"/> to operate on.</param>
		/// <param name="match">Successful Regex Match.</param>
		/// <returns><paramref name="data"/> parameter updated with the parsed values.</returns>
		private static GeoModel ParseGeoDataObsoletion(this GeoModel data, Match match)
		{
			data.ObsoleteDescription = match.Groups["Obsolete"].Value;
			return data;
		}

		/// <summary>
		/// Parses Identity and ProjValue using named groups from a successful match
		/// and updates the corresponding fields of the <paramref name="data"/> parameter.
		/// </summary>
		/// <param name="data"><see cref="GeoModel"/> to operate on.</param>
		/// <param name="match">Successful Regex Match.</param>
		/// <returns><paramref name="data"/> parameter updated with the parsed values.</returns>
		private static GeoModel ParseGeoDataIdentity(this GeoModel data, Match match)
		{
			data.Identity = match.Groups["Identity"].Value;
			data.ProjectValue = match.Groups["ProjValue"].Value;
			return data;
		}
	}
}
