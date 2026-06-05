namespace Strazh.Domain
{
    public abstract class Relationship : IInspectable
    {
        public abstract string Type { get; }
        
        public string ToInspection() => $$"""{ "Type": {{Type.Inspect()}} }""";
    }

    public class HaveRelationship : Relationship
    {
        public override string Type => "HAVE";
    }

    public class InvokeRelationship : Relationship
    {
        public override string Type => "INVOKE";
    }

    public class ConstructRelationship : Relationship
    {
        public override string Type => "CONSTRUCT";
    }

    public class OfTypeRelationship : Relationship
    {
        public override string Type => "OF_TYPE";
    }

    public class DeclaredAtRelationship : Relationship
    {
        public override string Type => "DECLARED_AT";
    }

    public class IncludedInRelationship : Relationship
    {
        public override string Type => "INCLUDED_IN";
    }

    public class DependsOnRelationship : Relationship
    {
        public override string Type => "DEPENDS_ON";
    }

    public class ContainsRelationship : Relationship
    {
        public override string Type => "CONTAINS";
    }

    public class ImplementsMethodRelationship : Relationship
    {
        public override string Type => "IMPLEMENTS_METHOD";
    }

    public class UsesTypeRelationship : Relationship
    {
        public override string Type => "USES_TYPE";
    }

    public class ExecutesRelationship : Relationship
    {
        public override string Type => "EXECUTES";
    }

    public class DefinesCommandRelationship : Relationship
    {
        public override string Type => "DEFINES_COMMAND";
    }
}