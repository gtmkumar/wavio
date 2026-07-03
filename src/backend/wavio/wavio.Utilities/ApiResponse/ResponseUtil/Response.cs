using System.Runtime.Serialization;
using wavio.Utilities.ApiResponse.IResponseUtil;

namespace wavio.Utilities.ApiResponse.ResponseUtil;

[DataContract]
public class Response : IResponse
{
    [DataMember(EmitDefaultValue = false)]
    public Message? Message { get; set; }

    [DataMember]
    public bool Status { get; set; }
}
