// No using IronMapper.Generated in this file: the package adds it as a global using (ImplicitUsings is enabled by the
// repository's Directory.Build.props). Calls mappings of this project and public mappings of PublicLib, so the global
// using must not make them ambiguous; Program.cs has the explicit using next to the global one.
public static class GlobalUsingCalls
{
    public static string Describe()
        => new UserEntity { Id = 1, Name = "g" }.MapToUserDto().FullName
           + new PublicLib.Product { Name = "p" }.MapToProductDto().Name
           + new PublicLib.Member { Name = "m" }.MapToAdminMemberDto().Name;
}
