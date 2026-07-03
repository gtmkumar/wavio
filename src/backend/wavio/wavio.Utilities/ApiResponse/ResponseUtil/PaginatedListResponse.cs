using System.Runtime.Serialization;
using wavio.Utilities.ApiResponse.IResponseUtil;
using wavio.Utilities.Common;

namespace wavio.Utilities.ApiResponse.ResponseUtil;

[DataContract]
public class PaginatedListResponse<TModel> : IPaginatedListResponse<TModel>
{
    [DataMember(EmitDefaultValue = false)]
    public Message? Message { get; set; }

    [DataMember]
    public bool Status { get; set; }

    [DataMember(EmitDefaultValue = false)]
    public PaginatedList<TModel>? Data { get; set; }
}
