using FluentAssertions;
using Quotes.Repository.Entities;
using Quotes.Model.Shared;
using Xunit;

namespace Tests.Domain;

public class QuoteTests
{
    [Fact]
    public void Create_WithValidData_ShouldSucceed()
    {
        var result = Quote.Create("Marcus Aurelius", "The obstacle is the way.");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Author.Should().Be("Marcus Aurelius");
        result.Value.Text.Should().Be("The obstacle is the way.");
        result.Value.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_WithEmptyText_ShouldFail()
    {
        var result = Quote.Create("Marcus Aurelius", "");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_WithTextExceeding1000Chars_ShouldFail()
    {
        var longText = new string('a', 1001);

        var result = Quote.Create("Marcus Aurelius", longText);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_WithEmptyAuthor_ShouldFail()
    {
        var result = Quote.Create("", "The obstacle is the way.");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_WithAuthorExceeding200Chars_ShouldFail()
    {
        var longAuthor = new string('a', 201);

        var result = Quote.Create(longAuthor, "The obstacle is the way.");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Text_HasNoPublicSetter_IsImmutableAfterCreation()
    {
        var property = typeof(Quote).GetProperty(nameof(Quote.Text));

        property.Should().NotBeNull();
        property!.SetMethod.Should().NotBeNull("EF Core needs the setter to materialize entities");
        property.SetMethod!.IsPublic.Should().BeFalse("Text cannot be changed after creation");
    }

    [Fact]
    public void Delete_ShouldSetIsDeletedToTrue()
    {
        var quote = Quote.Create("Marcus Aurelius", "The obstacle is the way.").Value!;

        quote.Delete();

        quote.IsDeleted.Should().BeTrue();
    }
}
