using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Tests.OpenSubsonic;

public static class SubsonicSchemaValidator
{
    private static readonly Dictionary<string, ElementDefinition> ElementDefinitions = new()
    {
        ["musicFolders"] = new ElementDefinition
        {
            TypeName = "MusicFolders",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["musicFolder"] = new FieldDefinition { Type = FieldType.Array, ItemType = "MusicFolder" }
            },
            RequiredAttributes = Array.Empty<string>()
        },
        ["MusicFolder"] = new ElementDefinition
        {
            TypeName = "MusicFolder",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String
            },
            RequiredAttributes = new[] { "id" }
        },
        ["indexes"] = new ElementDefinition
        {
            TypeName = "Indexes",
            Attributes = new Dictionary<string, FieldType>
            {
                ["lastModified"] = FieldType.Long,
                ["ignoredArticles"] = FieldType.String
            },
            RequiredAttributes = new[] { "lastModified", "ignoredArticles" }
        },
        ["index"] = new ElementDefinition
        {
            TypeName = "Index",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["artist"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Artist" }
            }
        },
        ["artist"] = new ElementDefinition
        {
            TypeName = "Artist",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["albumCount"] = FieldType.Integer
            },
            RequiredAttributes = new[] { "id", "name" }
        },
        ["artists"] = new ElementDefinition
        {
            TypeName = "ArtistsID3",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["index"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Index" }
            }
        },
        ["album"] = new ElementDefinition
        {
            TypeName = "AlbumWithSongsID3",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["artist"] = FieldType.String,
                ["artistId"] = FieldType.String,
                ["year"] = FieldType.Integer,
                ["genre"] = FieldType.String,
                ["coverArt"] = FieldType.String,
                ["songCount"] = FieldType.Integer,
                ["duration"] = FieldType.Integer,
                ["created"] = FieldType.DateTime,
                ["albumArtist"] = FieldType.String,
                ["albumArtistId"] = FieldType.String
            },
            RequiredAttributes = new[] { "id", "name" }
        },
        ["song"] = new ElementDefinition
        {
            TypeName = "Child",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["parent"] = FieldType.String,
                ["title"] = FieldType.String,
                ["artist"] = FieldType.String,
                ["album"] = FieldType.String,
                ["genre"] = FieldType.String,
                ["coverArt"] = FieldType.String,
                ["duration"] = FieldType.Integer,
                ["bitRate"] = FieldType.Integer,
                ["path"] = FieldType.String,
                ["fileSize"] = FieldType.Long,
                ["isDir"] = FieldType.Boolean,
                ["albumId"] = FieldType.String,
                ["artistId"] = FieldType.String,
                ["year"] = FieldType.Integer,
                ["track"] = FieldType.Integer,
                ["discNumber"] = FieldType.Integer,
                ["created"] = FieldType.DateTime,
                ["releaseDate"] = FieldType.DateTime,
                ["genreId"] = FieldType.String,
                ["crc32"] = FieldType.String,
                ["suffix"] = FieldType.String,
                ["contentType"] = FieldType.String,
                ["size"] = FieldType.Long
            },
            RequiredAttributes = new[] { "id" }
        },
        ["genres"] = new ElementDefinition
        {
            TypeName = "Genres",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["genre"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Genre" }
            }
        },
        ["Genre"] = new ElementDefinition
        {
            TypeName = "Genre",
            Attributes = new Dictionary<string, FieldType>
            {
                ["name"] = FieldType.String,
                ["songCount"] = FieldType.Integer,
                ["albumCount"] = FieldType.Integer
            },
            RequiredAttributes = new[] { "name" }
        },
        ["directory"] = new ElementDefinition
        {
            TypeName = "Directory",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["parent"] = FieldType.String,
                ["path"] = FieldType.String
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["child"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            },
            RequiredAttributes = new[] { "id", "name" }
        },
        ["license"] = new ElementDefinition
        {
            TypeName = "License",
            Attributes = new Dictionary<string, FieldType>
            {
                ["valid"] = FieldType.Boolean,
                ["email"] = FieldType.String,
                ["key"] = FieldType.String,
                ["expires"] = FieldType.DateTime,
                ["trial"] = FieldType.Boolean
            },
            RequiredAttributes = new[] { "valid" }
        },
        ["openSubsonicExtensions"] = new ElementDefinition
        {
            TypeName = "OpenSubsonicExtensions",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["extension"] = new FieldDefinition { Type = FieldType.Array, ItemType = "OpenSubsonicExtension" }
            }
        },
        ["OpenSubsonicExtension"] = new ElementDefinition
        {
            TypeName = "OpenSubsonicExtension",
            Attributes = new Dictionary<string, FieldType>
            {
                ["name"] = FieldType.String,
                ["version"] = FieldType.String,
                ["description"] = FieldType.String
            },
            RequiredAttributes = new[] { "name" }
        },
        ["playlists"] = new ElementDefinition
        {
            TypeName = "Playlists",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["playlist"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Playlist" }
            }
        },
        ["Playlist"] = new ElementDefinition
        {
            TypeName = "Playlist",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["owner"] = FieldType.String,
                ["public"] = FieldType.Boolean,
                ["songCount"] = FieldType.Integer,
                ["duration"] = FieldType.Integer,
                ["created"] = FieldType.DateTime,
                ["changed"] = FieldType.DateTime,
                ["comment"] = FieldType.String
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            },
            RequiredAttributes = new[] { "id", "name" }
        },
        ["playlist"] = new ElementDefinition
        {
            TypeName = "PlaylistWithSongs",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["owner"] = FieldType.String,
                ["public"] = FieldType.Boolean,
                ["songCount"] = FieldType.Integer,
                ["duration"] = FieldType.Integer,
                ["created"] = FieldType.DateTime,
                ["changed"] = FieldType.DateTime,
                ["comment"] = FieldType.String
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            },
            RequiredAttributes = new[] { "id", "name" }
        },
        ["lyrics"] = new ElementDefinition
        {
            TypeName = "Lyrics",
            Attributes = new Dictionary<string, FieldType>
            {
                ["artist"] = FieldType.String,
                ["title"] = FieldType.String
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["verse"] = new FieldDefinition { Type = FieldType.Array, ItemType = "LyricsVerse" }
            }
        },
        ["starred"] = new ElementDefinition
        {
            TypeName = "Starred",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["artist"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Artist" },
                ["album"] = new FieldDefinition { Type = FieldType.Array, ItemType = "AlbumChild" },
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["starred2"] = new ElementDefinition
        {
            TypeName = "Starred2",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["artist"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Artist" },
                ["album"] = new FieldDefinition { Type = FieldType.Array, ItemType = "AlbumChild" },
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["nowPlaying"] = new ElementDefinition
        {
            TypeName = "NowPlaying",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["entry"] = new FieldDefinition { Type = FieldType.Array, ItemType = "NowPlayingEntry" }
            }
        },
        ["user"] = new ElementDefinition
        {
            TypeName = "User",
            Attributes = new Dictionary<string, FieldType>
            {
                ["username"] = FieldType.String,
                ["email"] = FieldType.String,
                ["scrobbleEnabled"] = FieldType.Boolean,
                ["adminRole"] = FieldType.Boolean,
                ["settingsRole"] = FieldType.Boolean,
                ["downloadRole"] = FieldType.Boolean,
                ["uploadRole"] = FieldType.Boolean,
                ["playlistRole"] = FieldType.Boolean,
                ["coverArtRole"] = FieldType.Boolean,
                ["commentRole"] = FieldType.Boolean,
                ["podcastRole"] = FieldType.Boolean,
                ["streamRole"] = FieldType.Boolean,
                ["jukeboxRole"] = FieldType.Boolean,
                ["shareRole"] = FieldType.Boolean
            },
            RequiredAttributes = new[] { "username" }
        },
        ["albumList"] = new ElementDefinition
        {
            TypeName = "AlbumList",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["album"] = new FieldDefinition { Type = FieldType.Array, ItemType = "AlbumChild" }
            }
        },
        ["albumList2"] = new ElementDefinition
        {
            TypeName = "AlbumList2",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["album"] = new FieldDefinition { Type = FieldType.Array, ItemType = "AlbumChild" }
            }
        },
        ["internetRadioStations"] = new ElementDefinition
        {
            TypeName = "InternetRadioStations",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["internetRadioStation"] = new FieldDefinition { Type = FieldType.Array, ItemType = "InternetRadioStation" }
            }
        },
        ["InternetRadioStation"] = new ElementDefinition
        {
            TypeName = "InternetRadioStation",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["streamUrl"] = FieldType.String,
                ["homePageUrl"] = FieldType.String
            },
            RequiredAttributes = new[] { "id", "name", "streamUrl" }
        },
        ["scanStatus"] = new ElementDefinition
        {
            TypeName = "ScanStatus",
            Attributes = new Dictionary<string, FieldType>
            {
                ["scanning"] = FieldType.Boolean,
                ["count"] = FieldType.Integer
            },
            RequiredAttributes = new[] { "scanning", "count" }
        },
        ["shares"] = new ElementDefinition
        {
            TypeName = "Shares",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["share"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Share" }
            }
        },
        ["Share"] = new ElementDefinition
        {
            TypeName = "Share",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["url"] = FieldType.String,
                ["description"] = FieldType.String,
                ["username"] = FieldType.String,
                ["created"] = FieldType.DateTime,
                ["expires"] = FieldType.DateTime,
                ["lastVisited"] = FieldType.DateTime,
                ["visitCount"] = FieldType.Integer
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["entry"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            },
            RequiredAttributes = new[] { "id", "url", "username", "created", "visitCount" }
        },
        ["similarSongs"] = new ElementDefinition
        {
            TypeName = "SimilarSongs",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["similarSongs2"] = new ElementDefinition
        {
            TypeName = "SimilarSongs2",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["songsByGenre"] = new ElementDefinition
        {
            TypeName = "SongsByGenre",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["topSongs"] = new ElementDefinition
        {
            TypeName = "TopSongs",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["AlbumChild"] = new ElementDefinition
        {
            TypeName = "AlbumChild",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["name"] = FieldType.String,
                ["artist"] = FieldType.String,
                ["artistId"] = FieldType.String,
                ["coverArt"] = FieldType.String,
                ["year"] = FieldType.Integer,
                ["genre"] = FieldType.String,
                ["songCount"] = FieldType.Integer,
                ["duration"] = FieldType.Integer,
                ["created"] = FieldType.DateTime,
                ["albumArtist"] = FieldType.String,
                ["albumArtistId"] = FieldType.String,
                ["parent"] = FieldType.String
            },
            RequiredAttributes = new[] { "id", "name" }
        },
        ["randomSongs"] = new ElementDefinition
        {
            TypeName = "Songs",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["searchResult2"] = new ElementDefinition
        {
            TypeName = "SearchResult2",
            Attributes = new Dictionary<string, FieldType>
            {
                ["totalHits"] = FieldType.Integer,
                ["offset"] = FieldType.Integer
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["artist"] = new FieldDefinition { Type = FieldType.Array, ItemType = "artist" },
                ["album"] = new FieldDefinition { Type = FieldType.Array, ItemType = "AlbumChild" },
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "song" }
            }
        },
        ["searchResult3"] = new ElementDefinition
        {
            TypeName = "SearchResult3",
            Attributes = new Dictionary<string, FieldType>
            {
                ["totalHits"] = FieldType.Integer,
                ["offset"] = FieldType.Integer
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["artist"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Artist" },
                ["album"] = new FieldDefinition { Type = FieldType.Array, ItemType = "AlbumChild" },
                ["song"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        },
        ["podcasts"] = new ElementDefinition
        {
            TypeName = "Podcasts",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["channel"] = new FieldDefinition { Type = FieldType.Array, ItemType = "PodcastChannel" }
            }
        },
        ["PodcastChannel"] = new ElementDefinition
        {
            TypeName = "PodcastChannel",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["title"] = FieldType.String,
                ["description"] = FieldType.String,
                ["url"] = FieldType.String,
                ["coverArt"] = FieldType.String,
                ["originalImageUrl"] = FieldType.String,
                ["status"] = FieldType.String,
                ["errorMessage"] = FieldType.String
            },
            Children = new Dictionary<string, FieldDefinition>
            {
                ["episode"] = new FieldDefinition { Type = FieldType.Array, ItemType = "PodcastEpisode" }
            },
            RequiredAttributes = new[] { "id", "title", "url" }
        },
        ["PodcastEpisode"] = new ElementDefinition
        {
            TypeName = "PodcastEpisode",
            Attributes = new Dictionary<string, FieldType>
            {
                ["id"] = FieldType.String,
                ["channelId"] = FieldType.String,
                ["title"] = FieldType.String,
                ["description"] = FieldType.String,
                ["duration"] = FieldType.Integer,
                ["year"] = FieldType.Integer,
                ["genre"] = FieldType.String,
                ["coverArt"] = FieldType.String,
                ["size"] = FieldType.Long,
                ["contentType"] = FieldType.String,
                ["suffix"] = FieldType.String,
                ["path"] = FieldType.String,
                ["isDir"] = FieldType.Boolean,
                ["isVideo"] = FieldType.Boolean,
                ["playCount"] = FieldType.Integer,
                ["created"] = FieldType.DateTime,
                ["status"] = FieldType.String,
                ["album"] = FieldType.String,
                ["artist"] = FieldType.String,
                ["track"] = FieldType.Integer,
                ["bitRate"] = FieldType.Integer,
                ["discNumber"] = FieldType.Integer
            },
            RequiredAttributes = new[] { "id", "title" }
        },
        ["newestPodcasts"] = new ElementDefinition
        {
            TypeName = "NewestPodcasts",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["episode"] = new FieldDefinition { Type = FieldType.Array, ItemType = "PodcastEpisode" }
            }
        },
        ["jukeboxStatus"] = new ElementDefinition
        {
            TypeName = "JukeboxStatus",
            Attributes = new Dictionary<string, FieldType>
            {
                ["currentIndex"] = FieldType.Integer,
                ["playing"] = FieldType.Boolean,
                ["gain"] = FieldType.Double,
                ["position"] = FieldType.Integer
            },
            RequiredAttributes = new[] { "currentIndex", "playing", "gain" }
        },
        ["jukeboxPlaylist"] = new ElementDefinition
        {
            TypeName = "JukeboxPlaylist",
            Children = new Dictionary<string, FieldDefinition>
            {
                ["entry"] = new FieldDefinition { Type = FieldType.Array, ItemType = "Child" }
            }
        }
    };

    public static List<string> ValidateResponseElement(string elementName, JsonNode? element)
    {
        var errors = new List<string>();

        if (element == null)
        {
            errors.Add($"Element '{elementName}' is null");
            return errors;
        }

        if (!ElementDefinitions.TryGetValue(elementName, out var definition))
        {
            errors.Add($"Unknown element type '{elementName}' - cannot validate against XSD");
            return errors;
        }

        // Handle the case where the element is an array (e.g., openSubsonicExtensions returns an array directly)
        if (element is JsonArray topLevelArray)
        {
            // If this element has children defined with an array type, validate each item in the array
            if (definition.Children != null && definition.Children.Count > 0)
            {
                var childDef = definition.Children.First();
                if (childDef.Value.Type == FieldType.Array && childDef.Value.ItemType != null)
                {
                    foreach (var item in topLevelArray)
                    {
                        var itemErrors = ValidateResponseElement(childDef.Value.ItemType, item);
                        errors.AddRange(itemErrors.Select(e => $"  {e}"));
                    }
                }
            }
            return errors;
        }

        if (definition.Attributes != null)
        {
            foreach (var attr in definition.Attributes)
            {
                var attrValue = element[attr.Key];
                if (attrValue != null)
                {
                    var validationError = ValidateFieldType(elementName, attr.Key, attrValue, attr.Value);
                    if (validationError != null)
                    {
                        errors.Add(validationError);
                    }
                }
            }
        }

        if (definition.RequiredAttributes != null)
        {
            foreach (var requiredAttr in definition.RequiredAttributes)
            {
                if (element[requiredAttr] == null)
                {
                    errors.Add($"Element '{elementName}' is missing required attribute '{requiredAttr}'");
                }
            }
        }

        if (definition.Children != null)
        {
            foreach (var child in definition.Children)
            {
                var childValue = element[child.Key];
                if (childValue != null)
                {
                    if (child.Value.Type == FieldType.Array)
                    {
                        if (childValue is JsonArray arr)
                        {
                            foreach (var item in arr)
                            {
                                var itemErrors = ValidateResponseElement(child.Value.ItemType ?? child.Key, item);
                                errors.AddRange(itemErrors.Select(e => $"  {e}"));
                            }
                        }
                        else
                        {
                            errors.Add($"Element '{elementName}/{child.Key}' should be an array");
                        }
                    }
                }
            }
        }

        return errors;
    }

    private static string? ValidateFieldType(string elementName, string fieldName, JsonNode fieldValue, FieldType expectedType)
    {
        var kind = fieldValue.GetValueKind();
        switch (expectedType)
        {
            case FieldType.Integer:
                // Accept number or string representation of integer
                if (kind != JsonValueKind.Number)
                {
                    if (kind == JsonValueKind.String && int.TryParse(fieldValue.ToString(), out _))
                        break;
                    return $"Field '{elementName}/{fieldName}' should be an integer";
                }
                break;
            case FieldType.Long:
                // Accept number, string representation of long, or empty string (treated as 0)
                if (kind != JsonValueKind.Number)
                {
                    var strVal = fieldValue.ToString();
                    if (kind == JsonValueKind.String && (string.IsNullOrEmpty(strVal) || long.TryParse(strVal, out _)))
                        break;
                    return $"Field '{elementName}/{fieldName}' should be a long/integer";
                }
                break;
            case FieldType.Boolean:
                // Accept boolean or string representation ("true"/"false")
                if (kind != JsonValueKind.True && kind != JsonValueKind.False)
                {
                    if (kind == JsonValueKind.String && bool.TryParse(fieldValue.ToString(), out _))
                        break;
                    return $"Field '{elementName}/{fieldName}' should be a boolean";
                }
                break;
            case FieldType.Double:
                // Accept number or string representation of double
                if (kind != JsonValueKind.Number)
                {
                    if (kind == JsonValueKind.String && double.TryParse(fieldValue.ToString(), out _))
                        break;
                    return $"Field '{elementName}/{fieldName}' should be a double";
                }
                break;
            case FieldType.DateTime:
                var dateStr = fieldValue.ToString();
                if (!DateTime.TryParse(dateStr, out _))
                    return $"Field '{elementName}/{fieldName}' should be a valid dateTime ('{dateStr}' is invalid)";
                break;
        }
        return null;
    }
}

public class ElementDefinition
{
    public string TypeName { get; set; } = "";
    public Dictionary<string, FieldType>? Attributes { get; set; }
    public string[] RequiredAttributes { get; set; } = Array.Empty<string>();
    public Dictionary<string, FieldDefinition>? Children { get; set; }
}

public class FieldDefinition
{
    public FieldType Type { get; set; }
    public string? ItemType { get; set; }
}

public enum FieldType
{
    String,
    Integer,
    Long,
    Boolean,
    DateTime,
    Double,
    Array
}
