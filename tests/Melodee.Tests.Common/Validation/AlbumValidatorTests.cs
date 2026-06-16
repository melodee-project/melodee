using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Serilog;

namespace Melodee.Tests.Common.Validation;

public class AlbumValidatorTests : TestsBase
{
    public const int ShouldBeBitRate = 320;

    public static Album TestAlbum
        => new()
        {
            AlbumType = AlbumType.Album,
            Artist = new Artist(
                "Billy Joel",
                "Billy Joel".ToNormalizedString()!,
                null,
                null,
                14)
            {
                SearchEngineResultUniqueId = 12345
            },
            Directory = new FileSystemDirectoryInfo
            {
                Path = "/melodee_test/tests/",
                Name = "tests"
            },
            ViaPlugins = [nameof(AtlMetaTag)],
            OriginalDirectory = new FileSystemDirectoryInfo
            {
                Path = string.Empty,
                Name = string.Empty
            },
            Status = AlbumStatus.Ok,
            Images =
            [
                new ImageInfo
                {
                    CrcHash = "12345",
                    FileInfo = new FileSystemFileInfo
                    {
                        Name = "2020591499-01-Front.jpg",
                        Size = 12345
                    },
                    PictureIdentifier = PictureIdentifier.Front
                }
            ],
            Tags =
            [
                new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.AlbumArtist,
                    Value = "Billy Joel"
                },
                new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.Album,
                    Value = "Cold Spring Harbor"
                },
                new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.RecordingYear,
                    Value = "1971"
                }
            ],
            Id = Guid.Parse("78F60545-4C64-4CD3-A810-89BAD2F5EAB4"),
            Songs =
            [
                new Song
                {
                    CrcHash = "TestValue",
                    File = new FileSystemFileInfo
                    {
                        Name = string.Empty,
                        Size = 12345
                    },
                    MediaAudios =
                    [
                        new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.BitRate,
                            Value = ShouldBeBitRate
                        }
                    ],
                    Tags =
                    [
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.AlbumArtist,
                            Value = "Billy Joel"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.Album,
                            Value = "Cold Spring Harbor"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.RecordingYear,
                            Value = "1971"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.RecordingDate,
                            Value = "11/01/1971"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.TrackNumber,
                            Value = "1"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.DiscNumber,
                            Value = "1"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.DiscTotal,
                            Value = "1"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.Title,
                            Value = "She's Got a Way"
                        }
                    ]
                },
                new Song
                {
                    CrcHash = "TestValue2",
                    File = new FileSystemFileInfo
                    {
                        Name = string.Empty,
                        Size = 123456
                    },
                    MediaAudios =
                    [
                        new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.BitRate,
                            Value = ShouldBeBitRate
                        }
                    ],
                    Tags =
                    [
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.AlbumArtist,
                            Value = "Billy Joel"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.Album,
                            Value = "Cold Spring Harbor"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.RecordingYear,
                            Value = "1971"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.TrackNumber,
                            Value = "2"
                        },
                        new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.Title,
                            Value = "You Can Make Me Free"
                        }
                    ]
                }
            ]
        };

    [Theory]
    [InlineData("Album Title", "Something", 1, false)]
    [InlineData("Album Title", "11:11", 5, false)]
    [InlineData("Album Title", "11:11", 11, false)]
    [InlineData("Album Title", "The Song Title", 5, false)]
    [InlineData(null, null, 1, true)]
    [InlineData(null, "", 1, true)]
    [InlineData(null, " ", 1, true)]
    [InlineData(null, "   ", 1, true)]
    [InlineData("Album Title", "15", 15, true)]
    [InlineData("Album Title", "15 ", 15, true)]
    [InlineData("Album Title", "0005 Song Title", 5, true)]
    [InlineData("Album Title", "Song   Title", 5, true)]
    [InlineData("Album Title", "11 - Song Title", 11, true)]
    [InlineData("Album Title", "Song Title - Part II", 11, false)]
    [InlineData("Album Title", "Album Title", 1, false)]
    [InlineData("Album Title", "Album Title - 01 Song Title", 1, true)]
    [InlineData("Album Title", "I Can't Even Walk Without You Holding My Hand", 6, false)]
    [InlineData("Album Title", "'81 Camaro", 1, false)]
    [InlineData("Album Title", "'81 Camaro", 8, false)]
    [InlineData("Album Title", "'81 Camaro", 81, false)]
    [InlineData("Album Title", "Album Title (prod DJ Stinky)", 5, true)]
    [InlineData("Album Title", "Production Blues", 5, false)]
    [InlineData("Album Title", "The Production Blues", 5, false)]
    [InlineData("Album Title", "Deep Delightful (DJ Andy De Gage Remix)", 5, false)]
    [InlineData("Album Title", "Left and Right (Feat. Jung Kook of BTS)", 5, true)]
    [InlineData("Album Title", "Left and Right ft. Jung Kook)", 5, true)]
    [InlineData("Album Title", "Karakondžula", 5, false)]
    [InlineData("Album Title", "Song■Title", 5, false)]
    [InlineData("Album Title", "Song💣Title", 5, false)]
    [InlineData("Album Title best of 48 years (Compiled and Mixed by DJ Stinky", "Song Title (Compiled and Mixed by DJ Stinky)", 5, false)]
    [InlineData("Megamix Chart Hits Best Of 12 Years (Compiled and Mixed by DJ Fl", "Megamix Chart Hits Best Of 12 Years (Compiled and Mixed by DJ Flimflam)", 5, false)]
    [InlineData("Album Title", "Flowers (Demo)", 11, false)]
    public void SongHasUnwantedText(string? AlbumTitle, string? SongName, int? SongNumber, bool shouldBe)
    {
        Assert.Equal(shouldBe, AlbumValidator.SongHasUnwantedText(AlbumTitle, SongName, SongNumber));
    }

    [Theory]
    [InlineData("A Stone's Throw", false)]
    [InlineData("Broken Arrow", false)]
    [InlineData("American Music Vol. 1", false)]
    [InlineData("Retro", false)]
    [InlineData("Eternally Gifted", false)]
    [InlineData("Electric Deluge, Vol. 2", false)]
    [InlineData("Album■Title", false)]
    [InlineData("Album💣Title", false)]
    [InlineData("The Fine Art Of Self Destruction", false)]
    [InlineData("Experience Yourself", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("   ", true)]
    [InlineData("Album Title Digipak", true)]
    [InlineData("Album Title digipak", true)]
    [InlineData("Album Title diGIpaK", true)]
    [InlineData("Monarch Deluxe Edition", true)]
    [InlineData("Monarch Re-Master", true)]
    [InlineData("Monarch Target Edition", true)]
    [InlineData("Monarch Remastered", true)]
    [InlineData("Monarch Re-mastered", true)]
    [InlineData("Monarch Album", true)]
    [InlineData("Monarch Remaster", true)]
    [InlineData("Monarch Reissue", true)]
    [InlineData("Monarch (REISSUE)", true)]
    [InlineData("Monarch Expanded", true)]
    [InlineData("Monarch (Expanded)", true)]
    [InlineData("Monarch (Expanded", true)]
    [InlineData("Monarch WEB", true)]
    [InlineData("Monarch REMASTERED", true)]
    [InlineData("Monarch (REMASTERED)", true)]
    [InlineData("Monarch [REMASTERED]", true)]
    [InlineData("Michael Bublé - Higher (Deluxe)", true)]
    [InlineData("Necro Sapiens (320)", true)]
    [InlineData("Necro Sapiens Compilation", true)]
    [InlineData("Necro Sapiens (Compilation)", true)]
    [InlineData("Captain Morgan's Revenge(Limited Japanese Editiom) (10th Anniversary Edition)", true)]
    [InlineData("Captain Morgan's Revenge (Limited Japanese Editiom) (10th Anniversary Edition)", true)]
    [InlineData("Hard work (EP)", true)]
    [InlineData("Hard work EP", true)]
    [InlineData("Experience Yourself Ep", true)]
    [InlineData("Experience Yourself LP", true)]
    [InlineData("Experience Yourself (Single)", true)]
    [InlineData("Escape (Deluxe Edition)", true)]
    [InlineData("Escape (Deluxe)", true)]
    [InlineData("Arsenal of Glory (Re-Edition)", true)]
    [InlineData("Arsenal of Glory (2005 Edition)", true)]
    [InlineData("The Bitch Is Back (Remastered 2017)", true)]
    public void AlbumTitleHasUnwantedText(string? AlbumTitle, bool shouldBe)
    {
        Assert.Equal(shouldBe, AlbumValidator.AlbumTitleHasUnwantedText(AlbumTitle));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Something", false)]
    [InlineData("Eternally Gifted", false)]
    [InlineData("Shift Scene", false)]
    [InlineData("Something With Bob", true)]
    [InlineData("Something Ft Bob", true)]
    [InlineData("Something ft Bob", true)]
    [InlineData("Something Ft. Bob", true)]
    [InlineData("Something (Ft. Bob)", true)]
    [InlineData("Something Feat. Bob", true)]
    [InlineData("Something Featuring Bob", true)]
    [InlineData("Something (with Bob)", true)]
    [InlineData("Minds Without Fear with Vishal-Shekhar", true)]
    public void StringHasFeaturingFragments(string? input, bool shouldBe)
    {
        Assert.Equal(AlbumValidator.StringHasFeaturingFragments(input), shouldBe);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("Song Title", false)]
    [InlineData("cover-PROOF.jpg", true)]
    [InlineData("cover proof.jpg", true)]
    [InlineData("proof.jpg", true)]
    [InlineData("proof image.jpg", true)]
    [InlineData("00-master_blaster-we_love_italo_disco-cd-flac-2003-proof.jpg", true)]
    [InlineData("cover.jpg", false)]
    [InlineData("00-big_ed_the_assassin-edward_lee_knight_1971-2001-2001-proof-cr_int", true)]
    public void IsImageProofType(string? text, bool shouldBe)
    {
        Assert.Equal(shouldBe, ImageValidator.IsImageAProofType(text));
    }

    private Album NewTestAlbum()
    {
        var serializer = new Serializer(Log.Logger);
        return serializer.Deserialize<Album>(serializer.Serialize(TestAlbum)) ?? throw new InvalidOperationException();
    }

    [Fact]
    public void ValidateAlbumWithNoInvalidValidations()
    {
        var album = NewTestAlbum();
        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(album);
        Assert.True(validationResult.IsSuccess);
        Assert.Equal(AlbumStatus.Invalid, validationResult.Data.AlbumStatus);
        Assert.Equal(AlbumNeedsAttentionReasons.HasInvalidSongs | AlbumNeedsAttentionReasons.HasNoImages, validationResult.Data.AlbumStatusReasons);
    }

    [Fact]
    public void ValidateAlbumWithNoCoverImage()
    {
        var testAlbum = NewTestAlbum();
        var album = testAlbum with
        {
            Images = []
        };
        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(album);
        Assert.True(validationResult.IsSuccess);
        Assert.Equal(AlbumStatus.Invalid, validationResult.Data.AlbumStatus);
        Assert.Equal(AlbumNeedsAttentionReasons.HasInvalidSongs | AlbumNeedsAttentionReasons.HasNoImages, validationResult.Data.AlbumStatusReasons);
    }

    [Fact]
    public void ValidateAlbumWithMissingArtist()
    {
        var testAlbum = NewTestAlbum();
        var albumTags = (testAlbum.Tags ?? []).ToList();
        albumTags.Remove(new MetaTag<object?>
        {
            Identifier = MetaTagIdentifier.AlbumArtist,
            Value = "Billy Joel"
        });
        var album = TestAlbum with
        {
            Artist = new Artist(string.Empty, string.Empty, null),
            Tags = albumTags
        };

        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(album);
        Assert.True(validationResult.IsSuccess);
        Assert.Equal(AlbumStatus.Invalid, validationResult.Data.AlbumStatus);
        Assert.Equal(AlbumNeedsAttentionReasons.HasInvalidArtists | AlbumNeedsAttentionReasons.HasInvalidSongs | AlbumNeedsAttentionReasons.HasNoImages, validationResult.Data.AlbumStatusReasons);
    }

    [Fact]
    public void ValidateAlbum_WithItunesArtistIdWithoutSearchEngineResult_DoesNotFlagInvalidArtist()
    {
        var album = NewTestAlbum();
        var artistName = "iTunes Artist";
        album.Artist = new Artist(
            artistName,
            artistName.ToNormalizedString()!,
            artistName)
        {
            ItunesId = "123456789"
        };

        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(album);

        Assert.True(validationResult.IsSuccess);
        Assert.False(validationResult.Data.AlbumStatusReasons.HasFlag(AlbumNeedsAttentionReasons.HasInvalidArtists));
    }

    [Fact]
    public void ValidateAlbumWithInvalidYear()
    {
        var testAlbum = NewTestAlbum();
        var albumTags = (testAlbum.Tags ?? []).ToList();
        var yearTag = albumTags.First(x => x.Identifier == MetaTagIdentifier.RecordingYear);
        albumTags.Remove(yearTag);
        var album = testAlbum with
        {
            Tags = albumTags
        };

        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(album);
        Assert.True(validationResult.IsSuccess);
        Assert.Equal(AlbumStatus.Invalid, validationResult.Data.AlbumStatus);
        Assert.Equal(AlbumNeedsAttentionReasons.HasInvalidSongs | AlbumNeedsAttentionReasons.HasInvalidYear | AlbumNeedsAttentionReasons.HasNoImages, validationResult.Data.AlbumStatusReasons);
    }

    [Fact]
    public void ValidateAlbumWithDifferentArtistsNotVariousArtists()
    {
        var testAlbum = NewTestAlbum();
        testAlbum.SetSongTagValue(testAlbum.Songs!.First().Id, MetaTagIdentifier.AlbumArtist, Guid.NewGuid().ToString());
        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(testAlbum);
        Assert.True(validationResult.IsSuccess);
        Assert.Equal(AlbumStatus.Invalid, validationResult.Data.AlbumStatus);
    }


    [Fact]
    public void ValidateAlbumWithMissingTitle()
    {
        var testAlbum = NewTestAlbum();
        var albumTags = (testAlbum.Tags ?? []).ToList();
        albumTags.Remove(new MetaTag<object?>
        {
            Identifier = MetaTagIdentifier.Album,
            Value = "Cold Spring Harbor"
        });
        var album = new Album
        {
            Artist = testAlbum.Artist,
            Directory = testAlbum.Directory,
            Tags = albumTags,
            Songs = testAlbum.Songs,
            ViaPlugins = testAlbum.ViaPlugins,
            OriginalDirectory = testAlbum.OriginalDirectory
        };

        var validator = new AlbumValidator(NewPluginsConfiguration());
        var validationResult = validator.ValidateAlbum(album);
        Assert.True(validationResult.IsSuccess);
        Assert.Equal(AlbumStatus.Invalid, validationResult.Data.AlbumStatus);
    }

    [Theory]
    [InlineData("A simple song title", "A simple song title")]
    [InlineData("Flowers   (DEMO)", "Flowers (DEMO)")]
    [InlineData("Bless em With The Blade (Orchestral Version)", "Bless em With The Blade (Orchestral Version)")]
    public void ValidateSongTitleReplacement(string input, string shouldBe)
    {
        Assert.Equal(shouldBe, AlbumValidator.RemoveUnwantedTextFromSongTitle(input));
    }
}
