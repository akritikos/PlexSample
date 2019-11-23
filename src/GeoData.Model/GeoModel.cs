namespace Kritikos.GeoData.Model
{
	public class GeoModel
	{
		public string Name { get; set; } = string.Empty;

		public string Description { get; set; } = string.Empty;

		public double MinimumLatitude { get; set; }

		public double MaximumLatitude { get; set; }

		public double MinimumLongitude { get; set; }

		public double MaximumLongitude { get; set; }

		public string ObsoleteDescription { get; set; } = string.Empty;

		public string Identity { get; set; } = string.Empty;

		public string ProjectValue { get; set; } = string.Empty;
	}
}
