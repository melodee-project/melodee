using System.Reflection;
using System.Xml.Linq;
using System.Xml.Schema;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Tests.Common.Services;
using NodaTime;
using Xunit.Abstractions;

namespace Melodee.Tests.Common.Models;

public class OpenSubsonicXmlSerializationTests(ITestOutputHelper testOutputHelper) : ServiceTestBase
{
    private XmlSchemaSet GetXmlSchemaSet()
    {
        var xsdPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Data", "subsonic-rest-api-1.16.1.xsd");
        var schemas = new XmlSchemaSet();
        schemas.Add("https://subsonic.org/restapi", xsdPath);
        return schemas;
    }

    [Fact]
    public void ValidateLicenseResponse()
    {
        var schemas = GetXmlSchemaSet();
        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = string.Empty,
                Data = new License(true, "admin@melodee.net", DateTimeOffset.UtcNow.AddYears(50).ToXmlSchemaDateTimeFormat(), string.Empty)
            }
        };
        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateUserResponse()
    {
        var schemas = GetXmlSchemaSet();
        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = string.Empty,
                Data = new User("dausername", false, "dauser@melodee.net", true, true, true, true, true, Instant.FromDateTimeUtc(DateTime.UtcNow).ToString())
            }
        };
        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateAlbumList2XmlResponse()
    {
        var xml = @"<subsonic-response
	xmlns=""https://subsonic.org/restapi"" status=""ok"" version=""1.16.1"">
	<albumList2>
		<album id=""album_d7f34197-9679-401f-a54d-570e319795ef"" name=""Darold"" coverArt=""album_d7f34197-9679-401f-a54d-570e319795ef"" songCount=""12"" playCount=""0"" year=""2024"" genre=""Hip-Hop"" created=""2024-12-03T00:14:35Z"" duration=""2410"" artist=""A$AP Ferg"" artistId=""artist_8fc92edc-a567-448b-bd83-582e251a9220""/>
		<album id=""album_ed096cfb-93aa-4c52-ab7e-54bed5c02321"" name=""Sapphire / Topaz"" coverArt=""album_ed096cfb-93aa-4c52-ab7e-54bed5c02321"" songCount=""2"" playCount=""0"" year=""2024"" genre=""Electronic"" created=""2024-12-03T00:14:35Z"" duration=""738"" artist=""Archelix"" artistId=""artist_ccd52eb9-7eaf-4981-93b9-ca2d2c74bae8""/>
		<album id=""album_fbf98213-bf26-4fc3-b697-12175399a9e3"" name=""Halloween Horror"" coverArt=""album_fbf98213-bf26-4fc3-b697-12175399a9e3"" songCount=""6"" playCount=""0"" year=""2024"" genre=""Rock"" created=""2024-12-03T00:14:35Z"" duration=""1689"" artist=""Alice Cooper"" artistId=""artist_d9fed9c1-05bf-4b1a-86c9-b33ce8312e96""/>
		<album id=""album_47b289e5-df81-4c5f-8dc8-52336d6a3a08"" name=""The Genuine Articulate"" coverArt=""album_47b289e5-df81-4c5f-8dc8-52336d6a3a08"" songCount=""8"" playCount=""0"" year=""2024"" genre=""Rap"" created=""2024-12-03T00:14:35Z"" duration=""1350"" artist=""The Alchemist"" artistId=""artist_f6a5a971-1311-4a00-a17d-1dcc9dce1297""/>
		<album id=""album_f8b92a6f-9925-427a-811b-496eb4ee1257"" name=""Birdtalker"" coverArt=""album_f8b92a6f-9925-427a-811b-496eb4ee1257"" songCount=""12"" playCount=""0"" year=""2021"" genre=""Folk Rock"" created=""2024-12-03T00:14:35Z"" duration=""2522"" artist=""Birdtalker"" artistId=""artist_c1f7bd22-e232-4778-b884-1184929babdc""/>
		<album id=""album_57a8fcb1-4142-46ef-b40b-44813a82b220"" name=""Mind Of A Country Boy"" coverArt=""album_57a8fcb1-4142-46ef-b40b-44813a82b220"" songCount=""14"" playCount=""0"" year=""2024"" created=""2024-12-03T00:14:35Z"" duration=""2798"" artist=""Luke Bryan"" artistId=""artist_0ddb716d-8cd3-436b-b51f-b703705cf0ef""/>
		<album id=""album_dfa1f075-e16a-438a-bd83-f3cd776ee79e"" name=""No Wake Zone"" coverArt=""album_dfa1f075-e16a-438a-bd83-f3cd776ee79e"" songCount=""5"" playCount=""0"" year=""2024"" created=""2024-12-03T00:14:35Z"" duration=""1013"" artist=""Zac Brown Band"" artistId=""artist_9a5429f3-624d-4625-b471-36d1df4c1567""/>
		<album id=""album_cfbb36e6-d19f-4d12-a0a0-b60feb05b1fb"" name=""Different Shades Of Blue (Overdrive)"" coverArt=""album_cfbb36e6-d19f-4d12-a0a0-b60feb05b1fb"" songCount=""14"" playCount=""0"" year=""2024"" genre=""Blues, Country, Folk"" created=""2024-12-03T00:14:35Z"" duration=""3935"" artist=""Joe Bonamassa"" artistId=""artist_85a1fc24-f83a-4e12-adc1-05a63b51ce33""/>
		<album id=""album_1da1417b-0e8c-4527-b8d1-81e4a4e5aadd"" name=""HIGH"" coverArt=""album_1da1417b-0e8c-4527-b8d1-81e4a4e5aadd"" songCount=""12"" playCount=""0"" year=""2024"" created=""2024-12-03T00:14:35Z"" duration=""2391"" artist=""Keith Urban"" artistId=""artist_f11fe6c5-42b6-496d-9291-609d28341565""/>
		<album id=""album_2f214dd5-6361-4dfb-bf3e-1256d091bdfe"" name=""Tokyo Night"" coverArt=""album_2f214dd5-6361-4dfb-bf3e-1256d091bdfe"" songCount=""6"" playCount=""0"" year=""2024"" genre=""Techno"" created=""2024-12-03T00:14:35Z"" duration=""2000"" artist=""Ayako Mori"" artistId=""artist_53ede65d-5107-490e-b615-8c03d48f5128""/>
	</albumList2>
</subsonic-response>";

        var schemas = GetXmlSchemaSet();
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateAlbumList2Response()
    {
        var schemas = GetXmlSchemaSet();
        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = "albumList2",
                DataDetailPropertyName = "album",
                Data = new[]
                {
                    new AlbumID3
                    {
                        Id = "1",
                        Name = "Da Album Name",
                        CoverArt = "cover_art:1",
                        SongCount = 5,
                        CreatedRaw = Instant.FromDateTimeUtc(DateTime.UtcNow),
                        Duration = 1327,
                        PlayCount = 0,
                        ArtistId = "a:1",
                        Artist = "Da Artist",
                        Year = 1980,
                        Starred = null,
                        Genres =
                        [
                            "Genre1",
                            "Genre2"
                        ]
                    },
                    new AlbumID3
                    {
                        Id = "2",
                        Name = "Da Album Name 2",
                        CoverArt = "cover_art:2",
                        SongCount = 6,
                        CreatedRaw = Instant.FromDateTimeUtc(DateTime.UtcNow),
                        Duration = 1328,
                        PlayCount = 1,
                        ArtistId = "a:2",
                        Artist = "Da Artist 2",
                        Year = 1981,
                        Starred = Instant.FromDateTimeUtc(DateTime.UtcNow).ToString(),
                        Genres =
                        [
                            "Genre2",
                            "Genre3"
                        ]
                    }
                }
            }
        };
        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }

    private Child NewChild()
    {
        return new Child("child1", "parent1", false, "title1", "album1", "artist1", 1, 1980, "coverart1",
            12345, "audio/mp3", "mp3", null, 4321, 320, 1, 44800, 2, "path1",
            1, DateTimeOffset.UtcNow.AddSeconds(5).ToXmlSchemaDateTimeFormat(), 1, DateTimeOffset.UtcNow.AddSeconds(1).ToXmlSchemaDateTimeFormat(),
            "albumid1", "artist1id", "music", "mediaType", false, 80, null, "sortname1", null, [new Genre { AlbumCount = 1, SongCount = 1, Value = "dagenre1" }],
            null, "displayartist1", null, "displayalbumartist1", null, null, null, null, 4, 2, "username1",
            3, 1, "feishin");
    }

    [Fact]
    public void ValidateRandomSongsXMLResponse()
    {
        var schemas = GetXmlSchemaSet();
        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = "randomSongs",
                DataDetailPropertyName = "song",
                Data = new[]
                {
                    NewChild()
                }
            }
        };

        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateSubsonicXmlPingResponse()
    {
        var schemas = GetXmlSchemaSet();
        var goodXml = "<subsonic-response xmlns=\"https://subsonic.org/restapi\" status=\"ok\" version=\"1.1.1\"> </subsonic-response>";
        var badXml = "<subsonic-response xmlns=\"https://subsonic.org/restapi\" _sXXXatus=\"batman\" version=\"1.1.1\"> </subsonic-response>";

        // Should be good
        var xmlDoc = XDocument.Load(new StringReader(goodXml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);

        // Should be bad
        xmlDoc = XDocument.Load(new StringReader(badXml));
        isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.False(isValid);

        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = string.Empty
            }
        };
        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        xmlDoc = XDocument.Load(new StringReader(xml));
        isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        // Should be good
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateBookmarkXmlResponse()
    {
        var schemas = GetXmlSchemaSet();
        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = "bookmarks",
                Data = new[]
                {
                    new Bookmark(0, "username1", null, Instant.FromDateTimeUtc(DateTime.UtcNow).ToString(), Instant.FromDateTimeUtc(DateTime.UtcNow).ToString(), [NewChild()])
                }
            }
        };
        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }

    [Fact]
    public void ValidatePlayQueueXmlResponse()
    {
        var schemas = GetXmlSchemaSet();
        var response = new ResponseModel
        {
            IsSuccess = true,
            UserInfo = new UserInfo(5, Guid.NewGuid(), "batman", "batman@melodee.net", string.Empty, string.Empty),
            ResponseData = new ApiResponse
            {
                IsSuccess = true,
                Version = "1.16.1",
                Type = "Melodee",
                ServerVersion = "v1.0.0-rc1",
                DataPropertyName = string.Empty,
                Data = new PlayQueue
                {
                    Current = 1,
                    Position = 1234,
                    ChangedBy = "username1",
                    Changed = Instant.FromDateTimeUtc(DateTime.UtcNow).ToString(),
                    Username = "username1",
                    Entry = [NewChild()]
                }
            }
        };
        var xml = Serializer.SerializeOpenSubsonicModelToXml(response);
        Assert.NotNull(xml);
        var xmlDoc = XDocument.Load(new StringReader(xml));
        var isValid = true;
        xmlDoc.Validate(schemas, (sender, e) =>
        {
            testOutputHelper.WriteLine("Validation error: {0}", e.Message);
            isValid = false;
        });
        Assert.True(isValid);
    }
}
