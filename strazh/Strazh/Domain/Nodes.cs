using System.Collections.Generic;
using System.Linq;

namespace Strazh.Domain
{
    public abstract class Node : IInspectable
    {
        public abstract string Label { get; }

        public virtual string FullName { get; }

        public virtual string Name { get; }

        /// <summary>
        /// Primary Key used to compare Matching of nodes on MERGE operation
        /// </summary>
        public virtual string Pk { get; protected set; }

        public Node(string fullName, string name)
        {
            FullName = fullName;
            Name = name;
            SetPrimaryKey();
        }

        protected static string StableHash(string text)
        {
            // FNV-1a 64-bit over UTF-16 code units (deterministic across processes/runtimes)
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString();
        }

        protected virtual void SetPrimaryKey()
        {
            Pk = StableHash(FullName);
        }

        public virtual string Set(string node) =>
            $"{node}.pk = \"{Pk}\", {node}.fullName = \"{FullName}\", {node}.name = \"{Name}\"";

        /// <summary>
        /// Extra scalar properties carried onto the Neo4j node (beyond pk/name/fullName).
        /// Mirrors what <see cref="Set"/> emits, but as a map so the NDJSON/batch load path
        /// (which does <c>SET n += row.props</c>) preserves them. Base nodes carry none.
        /// </summary>
        public virtual IReadOnlyDictionary<string, string> NodeProperties
            => new Dictionary<string, string>();

        public string ToInspection() =>
            $$"""{ "Pk": {{Pk.Inspect()}}, "Label": {{Label.Inspect()}}, "FullName": {{FullName.Inspect()}}, "Name": {{Name.Inspect()}} }""";

        private string[]? _extraLabels;
        public void AddRoleLabels(IEnumerable<string> roles) => _extraLabels = roles.ToArray();
        public IReadOnlyList<string> AllLabels =>
            _extraLabels == null ? new[] { Label } : new[] { Label }.Concat(_extraLabels).ToArray();
    }

    // Code

    public abstract class CodeNode : Node
    {
        public CodeNode(string fullName, string name, string[] modifiers = null)
            : base(fullName, name)
        {

            Modifiers = modifiers == null ? "" : string.Join(", ", modifiers);
        }

        public string Modifiers { get; }

        public override string Set(string node)
            => $"{base.Set(node)}{(string.IsNullOrEmpty(Modifiers) ? "" : $", {node}.modifiers = \"{Modifiers}\"")}";

        public override IReadOnlyDictionary<string, string> NodeProperties
        {
            get
            {
                var props = new Dictionary<string, string>(base.NodeProperties);
                if (!string.IsNullOrEmpty(Modifiers)) props["modifiers"] = Modifiers;
                return props;
            }
        }
    }

    public abstract class TypeNode : CodeNode
    {
        public TypeNode(string fullName, string name, string[] modifiers = null)
            : base(fullName, name, modifiers)
        {
        }
    }

    public class ClassNode : TypeNode
    {
        public ClassNode(string fullName, string name, string[] modifiers = null)
            : base(fullName, name, modifiers)
        {
        }

        public override string Label { get; } = "Class";
    }

    public class InterfaceNode : TypeNode
    {
        public InterfaceNode(string fullName, string name, string[] modifiers = null)
            : base(fullName, name, modifiers)
        {
        }

        public override string Label { get; } = "Interface";
    }

    public class MethodNode : CodeNode
    {
        public MethodNode(string fullName, string name, (string name, string type)[] args, string returnType, string[] modifiers = null)
            : base(fullName, name, modifiers)
        {
            Arguments = string.Join(", ", args.Select(x => $"{x.type} {x.name}"));
            ReturnType = returnType;
            SetPrimaryKey();
        }

        public override string Label { get; } = "Method";

        public string Arguments { get; }

        public string ReturnType { get; }

        public override string Set(string node)
            => $"{base.Set(node)}, {node}.returnType = \"{ReturnType}\", {node}.arguments = \"{Arguments}\"";

        public override IReadOnlyDictionary<string, string> NodeProperties
        {
            get
            {
                var props = new Dictionary<string, string>(base.NodeProperties)
                {
                    ["returnType"] = ReturnType,
                    ["arguments"] = Arguments,
                };
                return props;
            }
        }

        protected override void SetPrimaryKey()
        {
            Pk = StableHash($"{FullName}|{Arguments}|{ReturnType}");
        }
    }

    public class CommandNode : CodeNode
    {
        public CommandNode(string fullName, string name) : base(fullName, name) { }
        public override string Label { get; } = "Command";
    }

    // Structure

    public class FileNode : Node
    {
        public FileNode(string fullName, string name)
            : base(fullName, name) { }

        public override string Label { get; } = "File";
    }

    public class FolderNode : Node
    {
        public FolderNode(string fullName, string name)
            : base(fullName, name) { }

        public override string Label { get; } = "Folder";
    }

    public class SolutionNode(string name) : Node(name, name)
    {
        public override string Label => "Solution";
    }

    public class ProjectNode : Node
    {
        public ProjectNode(string name)
            : this(name, name) { }

        public ProjectNode(string fullName, string name)
            : base(fullName, name) { }

        public override string Label { get; } = "Project";
    }

    public class PackageNode : Node
    {
        public PackageNode(string fullName, string name, string version)
            : base(fullName, name)
        {
            Version = version;
            SetPrimaryKey();
        }

        public override string Label { get; } = "Package";

        public string Version { get; }

        public override string Set(string node)
            => $"{base.Set(node)}, {node}.version = \"{Version}\"";

        public override IReadOnlyDictionary<string, string> NodeProperties
            => new Dictionary<string, string>(base.NodeProperties) { ["version"] = Version };

        protected override void SetPrimaryKey()
        {
            Pk = StableHash($"{FullName}|{Version}");
        }
    }
}