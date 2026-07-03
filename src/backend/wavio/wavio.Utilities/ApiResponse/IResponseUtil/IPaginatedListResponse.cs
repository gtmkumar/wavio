using wavio.Utilities.Common;

namespace wavio.Utilities.ApiResponse.IResponseUtil;

public interface IPaginatedListResponse<TModel> : IResponse
{
    PaginatedList<TModel>? Data { get; set; }
}
