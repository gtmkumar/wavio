using WaGateway.Application.Messages.Dtos;
using WaGateway.Application.Messages.Logic;
using Xunit;

namespace WaGateway.Tests.Messages;

public class MessagePayloadValidatorTests
{
    [Fact]
    public void Unknown_message_type_returns_an_error()
    {
        var errors = MessagePayloadValidator.Validate("not_a_real_type", "{}");

        Assert.Single(errors);
    }

    [Fact]
    public void Malformed_json_returns_an_error_instead_of_throwing()
    {
        var errors = MessagePayloadValidator.Validate(MessageTypes.Text, "{not json");

        Assert.Single(errors);
    }

    [Fact]
    public void Text_payload_requires_a_body()
    {
        Assert.Empty(MessagePayloadValidator.Validate(MessageTypes.Text, """{"body":"hello"}"""));
        Assert.NotEmpty(MessagePayloadValidator.Validate(MessageTypes.Text, """{"body":""}"""));
    }

    [Theory]
    [InlineData("utility", true)]
    [InlineData("marketing", true)]
    [InlineData("authentication", true)]
    [InlineData("not_a_category", false)]
    public void Template_payload_requires_a_valid_category(string category, bool expectedValid)
    {
        var json = $$"""{"name":"order_ready","language":"en_US","category":"{{category}}"}""";

        var errors = MessagePayloadValidator.Validate(MessageTypes.Template, json);

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Fact]
    public void Media_payload_requires_either_mediaId_or_link()
    {
        Assert.Empty(MessagePayloadValidator.Validate(MessageTypes.Media, """{"mediaType":"image","mediaId":"abc"}"""));
        Assert.Empty(MessagePayloadValidator.Validate(MessageTypes.Media, """{"mediaType":"image","link":"https://example.com/x.jpg"}"""));
        Assert.NotEmpty(MessagePayloadValidator.Validate(MessageTypes.Media, """{"mediaType":"image"}"""));
    }

    [Fact]
    public void Interactive_buttons_payload_rejects_more_than_three_buttons()
    {
        var json = """
            {"bodyText":"Pick one","buttons":[
                {"id":"1","title":"A"},{"id":"2","title":"B"},
                {"id":"3","title":"C"},{"id":"4","title":"D"}
            ]}
            """;

        var errors = MessagePayloadValidator.Validate(MessageTypes.InteractiveButtons, json);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Location_payload_validates_latitude_and_longitude_ranges()
    {
        Assert.Empty(MessagePayloadValidator.Validate(MessageTypes.Location, """{"latitude":12.9,"longitude":77.6}"""));
        Assert.NotEmpty(MessagePayloadValidator.Validate(MessageTypes.Location, """{"latitude":190,"longitude":77.6}"""));
        Assert.NotEmpty(MessagePayloadValidator.Validate(MessageTypes.Location, """{"latitude":12.9,"longitude":-200}"""));
    }

    [Fact]
    public void Reaction_payload_allows_an_empty_emoji_to_remove_a_reaction()
    {
        var errors = MessagePayloadValidator.Validate(MessageTypes.Reaction, """{"messageId":"wamid.ABC","emoji":""}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Order_details_payload_requires_a_positive_amount()
    {
        Assert.Empty(MessagePayloadValidator.Validate(MessageTypes.OrderDetails, """{"referenceId":"ORD-1","amount":100,"currencyCode":"INR"}"""));
        Assert.NotEmpty(MessagePayloadValidator.Validate(MessageTypes.OrderDetails, """{"referenceId":"ORD-1","amount":0,"currencyCode":"INR"}"""));
    }
}
