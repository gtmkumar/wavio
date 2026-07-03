namespace core.Application.Identity.AccessControl;

/// <summary>
/// Action-column semantics for the permission matrix. The MODULE taxonomy (rows)
/// is data-driven from the <c>identity_access.modules</c> table (see
/// <see cref="ModuleMatrix"/>); this type only normalises permission verbs onto
/// the six action columns and parses module/action out of a permission code.
/// </summary>
public static class PermissionMatrix
{
    /// <summary>Matrix column order.</summary>
    public static readonly string[] Actions = ["view", "create", "edit", "delete", "approve", "export"];

    /// <summary>permission.action (segment after last dot) → matrix column(s).
    /// A <c>manage</c> verb implies full control (view/create/edit/delete).</summary>
    public static string[] Columns(string action) => action switch
    {
        "read" or "list"                          => ["view"],
        "create"                                  => ["create"],
        "update"                                  => ["edit"],
        "manage"                                  => ["view", "create", "edit", "delete"],
        "delete" or "cancel" or "deactivate" or "revoke" => ["delete"],
        "approve" or "refund" or "publish" or "grant" or "assign" => ["approve"],
        "export" or "refresh"                     => ["export"],
        "set_password" or "set_type" or "tag" or "inspect" or "perform" or "scan" or "adjust" => ["edit"],
        _                                          => ["view"],
    };

    public static string Module(string code)
    {
        var dot = code.IndexOf('.');
        return dot < 0 ? code : code[..dot];
    }

    public static string Action(string code)
    {
        var dot = code.LastIndexOf('.');
        return dot < 0 ? code : code[(dot + 1)..];
    }
}
