using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.SilentlyIgnoredConfig;
using Xunit;

namespace IronMapper.Tests.SilentlyIgnoredConfig
{
    public class ArticleRow
    {
        public bool Published { get; set; } = true;
        public string Headline { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;

        // [Ignore] on a source property in a profile mapping: 2.0.0 copied it.
        [Ignore]
        public string Draft { get; set; } = string.Empty;
    }

    public class ArticleCard
    {
        // MapFrom(ArticleFormat.Title): the source has a property of the same name, which 2.0.0 copied instead.
        public string Title { get; set; } = string.Empty;

        // MapFrom(ArticleFormat.Slug): no source property of the same name (2.0.0: CS1061 in the generated file).
        public string Slug { get; set; } = string.Empty;

        // [Ignore] in a profile mapping: 2.0.0 copied ArticleRow.Secret.
        [Ignore]
        public string Secret { get; set; } = string.Empty;

        public string Draft { get; set; } = string.Empty;

        // Set by AfterMap(ArticleFormat.Stamp): 2.0.0 never called it.
        public string Stamp { get; set; } = string.Empty;
    }

    public static class ArticleFormat
    {
        public static string Title(ArticleRow row) => row.Headline.ToUpperInvariant();

        public static string Slug(ArticleRow row) => row.Headline.Replace(' ', '-').ToLowerInvariant();

        public static bool IsPublished(ArticleRow row) => row.Published;

        public static void Stamp(ArticleRow row, ArticleCard card) => card.Stamp = "mapped:" + card.Slug;
    }

    public class ArticleProfile : MappingProfile
    {
        public ArticleProfile()
        {
            CreateMap<ArticleRow, ArticleCard>()
                .ForMember(d => d.Title, o => o.MapFrom(ArticleFormat.Title))
                .ForMember(d => d.Slug, o => o.MapFrom(ArticleFormat.Slug))
                .When(ArticleFormat.IsPublished)
                .AfterMap(ArticleFormat.Stamp);
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// Profile configuration that 2.0.0 dropped without a diagnostic (issues #59 and #61), built with the warnings of this
    /// project as errors: static method groups in <c>MapFrom</c>, <c>When</c> and <c>AfterMap</c> are called,
    /// <c>[Ignore]</c> on a destination or a source property is applied (new object and in place).
    /// </summary>
    public class SilentlyIgnoredConfigTests
    {
        private static ArticleRow Row(bool published = true)
            => new() { Published = published, Headline = "Hello World", Title = "copied", Secret = "s", Draft = "d" };

        [Fact]
        public void NewObject_MethodGroupsCalled_IgnoredPropertiesNotCopied()
        {
            var card = Row().MapToArticleCard();

            Assert.Equal("HELLO WORLD", card.Title);
            Assert.Equal("hello-world", card.Slug);
            Assert.Equal(string.Empty, card.Secret);
            Assert.Equal(string.Empty, card.Draft);
            Assert.Equal("mapped:hello-world", card.Stamp);
        }

        [Fact]
        public void When_MethodGroupFalse_NoObject()
        {
            Assert.Null(Row(published: false).MapToArticleCard());
        }

        [Fact]
        public void InPlace_MethodGroupsCalled_IgnoredPropertiesKeepTheirValues()
        {
            var card = new ArticleCard { Secret = "kept", Draft = "kept" };

            Row().MapToArticleCard(card);

            Assert.Equal("HELLO WORLD", card.Title);
            Assert.Equal("hello-world", card.Slug);
            Assert.Equal("kept", card.Secret);
            Assert.Equal("kept", card.Draft);
        }
    }
}
