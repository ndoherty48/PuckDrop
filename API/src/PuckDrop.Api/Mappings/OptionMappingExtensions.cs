using PuckDrop.Api.Contracts;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Mappings;

public static class OptionMappingExtensions
{
    public static OptionResponse ToResponse(this Option option) => new(
        option.OptionId, option.Text, option.SortOrder);
}
