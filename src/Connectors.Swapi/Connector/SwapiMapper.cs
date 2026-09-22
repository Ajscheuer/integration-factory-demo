// Mapper — AI + human owned. Explicit transformations only (slide 05 "example-crm-mapper.ts").
// Scaffolded once by integration-cli generate from the APPROVED mapping files; never regenerated while this file exists.
// Every transformation below is traceable to connectors/swapi/mapping/*.mapping.json, including the human decisions.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Connectors.Swapi.Generated;

namespace Connectors.Swapi
{
    public static class SwapiMapper
    {
        private const string ProviderKey = "swapi";
        private static readonly string[] Sentinels = { "unknown", "n/a", "none" };

        /// <summary>
        /// Person → Character (operation getCharacter).
        /// Approved mapping: connectors/swapi/mapping/getCharacter.mapping.json
        /// </summary>
        public static Product.Canonical.Character ToCharacter(Connectors.Swapi.Generated.Person source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new Product.Canonical.Character
            {
                ExternalId = IdFromUrl(source.Url) ?? throw new ConnectorException("Provider record has no usable id.", "invalid_response"),
                Provider = ProviderKey,
                Name = source.Name?.Trim() ?? string.Empty,
                HeightCm = ParseInt(source.Height),
                MassKg = ParseDouble(source.Mass),
                BirthYear = ParseBirthYear(source.Birth_year),
                Gender = ParseGender(source.Gender),
                HairColors = SplitHairColors(source.Hair_color),
                EyeColor = NullIfSentinel(source.Eye_color),
                HomeworldId = IdFromUrl(source.Homeworld),
                FilmIds = IdsFromUrls(source.Films),
                SourceUrl = source.Url,
                LastModifiedUtc = source.Edited?.ToUniversalTime(),
            };
        }

        /// <summary>
        /// PeoplePage → CharacterPage (operation searchCharacters).
        /// Approved mapping: connectors/swapi/mapping/searchCharacters.mapping.json
        /// </summary>
        public static Product.Canonical.CharacterPage ToCharacterPage(Connectors.Swapi.Generated.PeoplePage source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new Product.Canonical.CharacterPage
            {
                Items = (source.Results ?? Array.Empty<Person>()).Select(ToCharacter).ToList(),
                TotalCount = source.Count,
                // HUMAN DECISION: token encodes only the page number ("page=<n>"), never the provider host.
                NextPageToken = EncodePageToken(PageFromUrl(source.Next)),
            };
        }

        /// <summary>
        /// Planet → Planet (operation getPlanet).
        /// Approved mapping: connectors/swapi/mapping/getPlanet.mapping.json
        /// </summary>
        public static Product.Canonical.Planet ToPlanet(Connectors.Swapi.Generated.Planet source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new Product.Canonical.Planet
            {
                ExternalId = IdFromUrl(source.Url) ?? throw new ConnectorException("Provider record has no usable id.", "invalid_response"),
                Provider = ProviderKey,
                Name = source.Name?.Trim() ?? string.Empty,
                DiameterKm = ParseInt(source.Diameter),
                RotationPeriodHours = ParseDouble(source.Rotation_period),
                OrbitalPeriodDays = ParseDouble(source.Orbital_period),
                SurfaceWaterPercent = ParseDouble(source.Surface_water),
                Population = ParseLong(source.Population),
                Climates = SplitList(source.Climate),
                Terrains = SplitList(source.Terrain),
                SourceUrl = source.Url,
                LastModifiedUtc = source.Edited?.ToUniversalTime(),
            };
        }

        // ---- paging token (searchCharacters, request side) ----

        /// <summary>Decodes an opaque product page token back into a provider page number. Null/empty token = first page.</summary>
        public static int? DecodePageToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            try
            {
                var padded = token.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                return text.StartsWith("page=", StringComparison.Ordinal) && int.TryParse(text.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page > 0
                    ? page
                    : throw new ConnectorException("Page token is not valid.", "invalid_response");
            }
            catch (FormatException ex)
            {
                throw new ConnectorException("Page token is not valid.", "invalid_response", ex);
            }
        }

        private static string? EncodePageToken(int? page) =>
            page is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes($"page={page.Value}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static int? PageFromUrl(Uri? next)
        {
            if (next is null) return null;
            var query = next.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in query)
            {
                var idx = pair.IndexOf('=');
                if (idx > 0 && pair[..idx] == "page" && int.TryParse(pair[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
                    return page;
            }
            return null;
        }

        // ---- shared parsing helpers (policy: private static, pure) ----

        private static bool IsSentinel(string? value) =>
            string.IsNullOrWhiteSpace(value) || Sentinels.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        private static string? NullIfSentinel(string? value) => IsSentinel(value) ? null : value!.Trim();

        private static int? ParseInt(string? value) =>
            !IsSentinel(value) && int.TryParse(value!.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

        private static long? ParseLong(string? value) =>
            !IsSentinel(value) && long.TryParse(value!.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;

        private static double? ParseDouble(string? value) =>
            !IsSentinel(value) && double.TryParse(value!.Replace(",", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        private static List<string> SplitList(string? value) =>
            IsSentinel(value)
                ? new List<string>()
                : value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !IsSentinel(s)).ToList();

        // getCharacter/hairColors maps only "n/a" and "none" to empty; "unknown" is preserved as a token.
        private static List<string> SplitHairColors(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();

            var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (items.Length == 1 &&
                (string.Equals(items[0], "n/a", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(items[0], "none", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<string>();
            }

            return items.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private static string? IdFromUrl(Uri? url)
        {
            if (url is null) return null;
            var last = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return string.IsNullOrWhiteSpace(last) ? null : last;
        }

        private static List<string> IdsFromUrls(IEnumerable<Uri>? urls) =>
            (urls ?? Enumerable.Empty<Uri>()).Select(IdFromUrl).Where(id => id is not null).Select(id => id!).ToList();

        private static readonly Regex BirthYearPattern = new(@"^(\d+(\.\d+)?)(BBY|ABY)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>HUMAN DECISION: unparsable non-"unknown" values -> null; never throws.</summary>
        private static double? ParseBirthYear(string? value)
        {
            if (IsSentinel(value)) return null;
            var match = BirthYearPattern.Match(value!.Trim());
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var magnitude)) return null;
            return string.Equals(match.Groups[3].Value, "BBY", StringComparison.OrdinalIgnoreCase) ? -magnitude : magnitude;
        }

        /// <summary>HUMAN DECISION: "n/a" (droids) -> Other; only "unknown"/unrecognised values -> Unknown.</summary>
        private static Product.Canonical.Gender ParseGender(string? value)
        {
            var trimmed = value?.Trim();
            if (string.Equals(trimmed, "male", StringComparison.OrdinalIgnoreCase)) return Product.Canonical.Gender.Male;
            if (string.Equals(trimmed, "female", StringComparison.OrdinalIgnoreCase)) return Product.Canonical.Gender.Female;
            if (string.Equals(trimmed, "hermaphrodite", StringComparison.OrdinalIgnoreCase)) return Product.Canonical.Gender.Other;
            if (string.Equals(trimmed, "n/a", StringComparison.OrdinalIgnoreCase)) return Product.Canonical.Gender.Other;
            return Product.Canonical.Gender.Unknown;
        }
    }
}
