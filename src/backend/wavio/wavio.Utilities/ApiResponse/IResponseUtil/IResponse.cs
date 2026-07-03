using wavio.Utilities.ApiResponse.ResponseUtil;

namespace wavio.Utilities.ApiResponse.IResponseUtil;

public interface IResponse
{
    Message? Message { get; set; }
    bool Status { get; set; }
}
