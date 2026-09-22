// Behavioral tests — shared ownership (slide 05 "tests/connector/").
// One assertion per approved field mapping; one test per human decision. Reviewers check these against the mapping file.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Connectors.Swapi.Generated;
using Connectors.Swapi;
using Gender = Product.Canonical.Gender;

namespace Connectors.Swapi.Tests.Connector
{
    public class SwapiMapperTests
    {
        private static T Fixture<T>(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                   ?? throw new InvalidOperationException($"fixture {name} did not deserialize");
        }

        private static Person Person(Action<Person> mutate)
        {
            var p = Fixture<Person>("Person.json");
            mutate(p);
            return p;
        }

        // ---------- getCharacter ----------

        [Fact]
        public void ToCharacter_maps_the_recorded_fixture()
        {
            var source = Fixture<Person>("Person.json"); // Luke Skywalker

            var result = SwapiMapper.ToCharacter(source);

            Assert.Equal("1", result.ExternalId);
            Assert.Equal("swapi", result.Provider);
            Assert.Equal("Luke Skywalker", result.Name);
            Assert.Equal(172, result.HeightCm);
            Assert.Equal(77d, result.MassKg);
            Assert.Equal(-19d, result.BirthYear);
            Assert.Equal(Gender.Male, result.Gender);
            Assert.Equal(new[] { "blond" }, result.HairColors);
            Assert.Equal("blue", result.EyeColor);
            Assert.Equal("1", result.HomeworldId);
            Assert.Equal(new[] { "1", "2", "3", "6" }, result.FilmIds);
            Assert.Equal(source.Url, result.SourceUrl);
            Assert.Equal(source.Edited!.Value.ToUniversalTime(), result.LastModifiedUtc);
        }

        [Fact]
        public void ToCharacter_handles_provider_sentinels()
        {
            var source = Person(p =>
            {
                p.Height = "unknown";
                p.Mass = "";
                p.Birth_year = "unknown";
                p.Gender = "unknown";
                p.Hair_color = "n/a";
                p.Eye_color = "n/a";
                p.Homeworld = null;
                p.Films = null;
            });

            var result = SwapiMapper.ToCharacter(source);

            Assert.Null(result.HeightCm);
            Assert.Null(result.MassKg);
            Assert.Null(result.BirthYear);
            Assert.Equal(Gender.Unknown, result.Gender);
            Assert.Empty(result.HairColors);
            Assert.Null(result.EyeColor);
            Assert.Null(result.HomeworldId);
            Assert.Empty(result.FilmIds!);
        }

        [Fact]
        public void ToCharacter_maps_droid_gender_to_other()
        {
            // HUMAN DECISION: "n/a" (droids) -> Other, not Unknown.
            var result = SwapiMapper.ToCharacter(Fixture<Person>("Person.c3po.json"));
            Assert.Equal(Gender.Other, result.Gender);
        }

        [Fact]
        public void ToCharacter_maps_unparsable_birth_year_to_null()
        {
            // HUMAN DECISION: unparsable non-"unknown" birth_year -> null, never throws.
            var source = Person(p => p.Birth_year = "12BBY?");
            var result = SwapiMapper.ToCharacter(source);
            Assert.Null(result.BirthYear);
        }

        [Fact]
        public void ToCharacter_preserves_unknown_hair_color_token()
        {
            var source = Person(p => p.Hair_color = "unknown");
            var result = SwapiMapper.ToCharacter(source);
            Assert.Equal(new[] { "unknown" }, result.HairColors);
        }

        [Fact]
        public void ToCharacter_strips_thousands_separator_from_mass()
        {
            var result = SwapiMapper.ToCharacter(Fixture<Person>("Person.jabba.json"));
            Assert.Equal(1358d, result.MassKg);
        }

        // ---------- searchCharacters ----------

        [Fact]
        public void ToCharacterPage_maps_the_recorded_fixture()
        {
            var source = Fixture<PeoplePage>("PeoplePage.json"); // search=a, page 1 of 6

            var result = SwapiMapper.ToCharacterPage(source);

            Assert.Equal(58, result.TotalCount);                                   // count = total across pages
            Assert.Equal(source.Results!.Count, result.Items.Count);              // results[] -> items
            Assert.Equal("Luke Skywalker", result.Items.First().Name);            // order preserved, same rules as ToCharacter
            Assert.NotNull(result.NextPageToken);                                  // next != null -> token
            Assert.DoesNotContain("swapi.dev", result.NextPageToken);              // HUMAN DECISION: no provider host in the token
            Assert.Equal(2, SwapiMapper.DecodePageToken(result.NextPageToken));   // round-trips to provider page 2
        }

        [Fact]
        public void ToCharacterPage_handles_provider_sentinels()
        {
            var last = Fixture<PeoplePage>("PeoplePage.json");
            last.Next = null;
            last.Results = null;

            var result = SwapiMapper.ToCharacterPage(last);

            Assert.Null(result.NextPageToken);   // last page
            Assert.Empty(result.Items);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void DecodePageToken_treats_empty_as_first_page(string? token) => Assert.Null(SwapiMapper.DecodePageToken(token));

        [Theory]
        [InlineData("not-base64!")]
        [InlineData("aHR0cHM6Ly9zd2FwaS5kZXY")] // "https://swapi.dev" — a URL-shaped token is rejected
        public void DecodePageToken_rejects_foreign_tokens(string token)
        {
            var ex = Assert.Throws<ConnectorException>(() => SwapiMapper.DecodePageToken(token));
            Assert.Equal("invalid_response", ex.Code);
        }

        // ---------- getPlanet ----------

        [Fact]
        public void ToPlanet_maps_the_recorded_fixture()
        {
            var source = Fixture<Connectors.Swapi.Generated.Planet>("Planet.json"); // Tatooine

            var result = SwapiMapper.ToPlanet(source);

            Assert.Equal("1", result.ExternalId);
            Assert.Equal("swapi", result.Provider);
            Assert.Equal("Tatooine", result.Name);
            Assert.Equal(10465, result.DiameterKm);
            Assert.Equal(23d, result.RotationPeriodHours);
            Assert.Equal(304d, result.OrbitalPeriodDays);
            Assert.Equal(1d, result.SurfaceWaterPercent);
            Assert.Equal(200000L, result.Population);
            Assert.Equal(new[] { "arid" }, result.Climates);
            Assert.Equal(new[] { "desert" }, result.Terrains);
            Assert.Equal(source.Url, result.SourceUrl);
            Assert.Equal(source.Edited!.Value.ToUniversalTime(), result.LastModifiedUtc);
        }

        [Fact]
        public void ToPlanet_handles_provider_sentinels()
        {
            var source = Fixture<Connectors.Swapi.Generated.Planet>("Planet.json");
            source.Diameter = "unknown"; source.Rotation_period = "unknown"; source.Orbital_period = "";
            source.Surface_water = "unknown"; source.Population = "unknown"; source.Climate = "unknown"; source.Terrain = "unknown";

            var result = SwapiMapper.ToPlanet(source);

            Assert.Null(result.DiameterKm);
            Assert.Null(result.RotationPeriodHours);
            Assert.Null(result.OrbitalPeriodDays);
            Assert.Null(result.SurfaceWaterPercent);
            Assert.Null(result.Population);
            Assert.Empty(result.Climates);
            Assert.Empty(result.Terrains);
        }

        [Fact]
        public void ToPlanet_population_fits_in_long_and_splits_lists()
        {
            var source = Fixture<Connectors.Swapi.Generated.Planet>("Planet.json");
            source.Population = "1000000000000"; // Coruscant
            source.Climate = "temperate, tropical";
            var result = SwapiMapper.ToPlanet(source);
            Assert.Equal(1_000_000_000_000L, result.Population);
            Assert.Equal(new[] { "temperate", "tropical" }, result.Climates);
        }
    }
}
