using FluentAssertions;
using Quotes.Repository.Entities;
using Quotes.Model.Shared;
using Xunit;

namespace Quotes.Tests.Unit.Domain;

public class QuoteFactoryTests
{
    // ──────────────────────────────────────────────────────────────────
    //  Create – valid inputs
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidAuthorAndText_ReturnsSuccess()
    {
        // Arrange
        const string author = "Marcus Aurelius";
        const string text   = "The obstacle is the way.";

        // Act
        var result = Quote.Create(author, text);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Author.Should().Be(author);
        result.Value.Text.Should().Be(text);
    }

    [Fact]
    public void Create_WithOwnerId_StampsOwnerId()
    {
        // Arrange & Act
        var result = Quote.Create("Author", "Text", ownerId: 42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.OwnerId.Should().Be(42);
    }

    [Fact]
    public void Create_WithNullOwnerId_OwnedByNoOne()
    {
        // Arrange & Act
        var result = Quote.Create("Author", "Text", ownerId: null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.OwnerId.Should().BeNull();
    }

    [Fact]
    public void Create_AuthorAtExactly200Chars_ReturnsSuccess()
    {
        // Arrange — boundary: 200 is the maximum allowed length
        var author = new string('A', 200);

        // Act
        var result = Quote.Create(author, "Valid text");

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_TextAtExactly1000Chars_ReturnsSuccess()
    {
        // Arrange — boundary: 1000 is the maximum allowed length
        var text = new string('T', 1000);

        // Act
        var result = Quote.Create("Valid Author", text);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Create – invalid author (Theory covers null / empty / whitespace)
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyOrWhitespaceAuthor_ReturnsFailure(string? author)
    {
        // Arrange & Act
        var result = Quote.Create(author!, "Valid text");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_AuthorExceeds200Chars_ReturnsFailure()
    {
        // Arrange
        var tooLong = new string('A', 201);

        // Act
        var result = Quote.Create(tooLong, "Valid text");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Author");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Create – invalid text (Theory covers null / empty / whitespace)
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyOrWhitespaceText_ReturnsFailure(string? text)
    {
        // Arrange & Act
        var result = Quote.Create("Valid Author", text!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_TextExceeds1000Chars_ReturnsFailure()
    {
        // Arrange
        var tooLong = new string('T', 1001);

        // Act
        var result = Quote.Create("Valid Author", tooLong);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Text");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Initial state & immutability
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_NewQuote_IsNotDeleted()
    {
        // Arrange & Act
        var result = Quote.Create("Author", "Text");

        // Assert
        result.Value!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void TextProperty_HasPrivateSetter_EnforcesImmutability()
    {
        // Arrange
        var setter = typeof(Quote).GetProperty(nameof(Quote.Text))?.SetMethod;

        // Assert
        setter.Should().NotBeNull("EF Core needs the setter to materialise entities");
        setter!.IsPublic.Should().BeFalse("Text must not be mutable from outside the class");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Soft delete
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ActiveQuote_SetsIsDeletedTrue()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text").Value!;

        // Act
        quote.Delete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Delete_AlreadyDeletedQuote_RemainsDeleted()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text").Value!;
        quote.Delete();

        // Act – second call must be idempotent
        quote.Delete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Update
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ValidData_ReturnsSuccessAndMutatesQuote()
    {
        // Arrange
        var quote = Quote.Create("Old Author", "Old text").Value!;

        // Act
        var result = quote.Update("New Author", "New text");

        // Assert
        result.IsSuccess.Should().BeTrue();
        quote.Author.Should().Be("New Author");
        quote.Text.Should().Be("New text");
    }

    [Theory]
    [InlineData("", "Valid text")]
    [InlineData("Valid Author", "")]
    public void Update_InvalidData_ReturnsFailure(string author, string text)
    {
        // Arrange
        var quote = Quote.Create("Author", "Text").Value!;

        // Act
        var result = quote.Update(author, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
