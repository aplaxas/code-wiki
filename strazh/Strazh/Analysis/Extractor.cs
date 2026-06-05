using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using Strazh.Domain;
using System.IO;

namespace Strazh.Analysis
{
    public static class Extractor
    {
        private static TypeNode CreateTypeNode(this ISymbol symbol, TypeDeclarationSyntax declaration)
        {
            (string fullName, string name) = (symbol.ContainingNamespace.ToString() + '.' + symbol.Name, symbol.Name);
            switch (declaration)
            {
                case ClassDeclarationSyntax _:
                    return new ClassNode(fullName, name, declaration.Modifiers.MapModifiers());
                case InterfaceDeclarationSyntax _:
                    return new InterfaceNode(fullName, name, declaration.Modifiers.MapModifiers());
            }
            return null;
        }

        private static ClassNode CreateClassNode(this TypeInfo typeInfo)
            => new ClassNode(GetFullName(typeInfo), GetName(typeInfo));

        private static InterfaceNode CreateInterfaceNode(this TypeInfo typeInfo)
            => new InterfaceNode(GetFullName(typeInfo), GetName(typeInfo));

        private static string[] MapModifiers(this SyntaxTokenList syntaxTokens)
            => syntaxTokens.Select(x => x.ValueText).ToArray();

        private static TypeNode CreateTypeNode(this TypeInfo typeInfo)
        {
            switch (typeInfo.ConvertedType.TypeKind)
            {
                case TypeKind.Interface:
                    return CreateInterfaceNode(typeInfo);

                case TypeKind.Class:
                    return CreateClassNode(typeInfo);

                default:
                    return null;
            }
        }

        private static string GetName(this TypeInfo typeInfo)
            => typeInfo.Type.Name;

        private static string GetFullName(this TypeInfo typeInfo)
            => typeInfo.Type.ContainingNamespace.ToString() + "." + GetName(typeInfo);

        private static string GetNamespaceName(this INamespaceSymbol namespaceSymbol, string name)
        {
            var nextName = namespaceSymbol?.Name;
            if (string.IsNullOrEmpty(nextName))
            {
                return name;
            }
            return GetNamespaceName(namespaceSymbol.ContainingNamespace, $"{nextName}.{name}");
        }

        private static MethodNode CreateMethodNode(this IMethodSymbol symbol, MethodDeclarationSyntax declaration = null)
        {
            var temp = $"{symbol.ContainingType}.{symbol.Name}";
            var fullName = symbol.ContainingNamespace.GetNamespaceName($"{symbol.ContainingType.Name}.{symbol.Name}");
            var args = symbol.Parameters.Select(x => (name: x.Name, type: x.Type.ToString())).ToArray();
            var returnType = symbol.ReturnType.ToString();
            return new MethodNode(fullName,
                symbol.Name,
                args,
                returnType,
                declaration?.Modifiers.MapModifiers());
        }

        public static MethodNode ToMethodNode(this IMethodSymbol symbol)
            => symbol.CreateMethodNode();

        private static bool IsDomainType(ITypeSymbol? type, out INamedTypeSymbol named)
        {
            named = (type as INamedTypeSymbol)!;
            if (named == null) return false;
            if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Interface) return false;
            var ns = named.ContainingNamespace?.ToString() ?? "";
            if (ns.StartsWith("System") || ns.StartsWith("Microsoft")) return false;
            return true;
        }

        private static TypeNode ToTypeNode(this INamedTypeSymbol named)
        {
            var fullName = (named.ContainingNamespace?.ToString() ?? "") + "." + named.Name;
            return named.TypeKind == TypeKind.Interface
                ? new InterfaceNode(fullName, named.Name)
                : new ClassNode(fullName, named.Name);
        }

        /// <summary>*Command 타입의 객체 생성에서 Command 멤버명과 핸들러 메서드를 연결.</summary>
        public static void GetCommands(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
        {
            if (sem.GetDeclaredSymbol(declaration) is not INamedTypeSymbol owner) return;
            var ownerFullName = (owner.ContainingNamespace?.ToString() ?? "") + "." + owner.Name;
            var ownerNode = new ClassNode(ownerFullName, owner.Name);

            foreach (var creation in declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var typeName = creation.Type.ToString();
                if (!typeName.Contains("Command")) continue;

                // 대상 Command 멤버명: 할당식 좌변 또는 프로퍼티/필드 이니셜라이저
                string? commandName = creation.Ancestors()
                    .OfType<AssignmentExpressionSyntax>()
                    .Select(a => (a.Left as IdentifierNameSyntax)?.Identifier.Text)
                    .FirstOrDefault(n => n != null);
                commandName ??= creation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                             ?? creation.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.Text;
                if (commandName == null) continue;

                var commandNode = new CommandNode($"{ownerFullName}.{commandName}", commandName);
                triples.Add(new TripleDefinesCommand(ownerNode, commandNode));

                var firstArg = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                if (firstArg == null) continue;
                var info = sem.GetSymbolInfo(firstArg);
                if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol handler)
                    triples.Add(new TripleExecutes(commandNode, handler.ToMethodNode()));
            }
        }

        /// <summary>메서드 파라미터/반환 타입의 도메인 타입 참조를 USES_TYPE으로 추출.</summary>
        public static void GetTypeUsages(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
        {
            foreach (var method in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (sem.GetDeclaredSymbol(method) is not IMethodSymbol m) continue;
                var methodNode = m.ToMethodNode();
                foreach (var p in m.Parameters)
                    if (IsDomainType(p.Type, out var nt))
                        triples.Add(new TripleUsesType(methodNode, nt.ToTypeNode()));
                if (IsDomainType(m.ReturnType, out var rt))
                    triples.Add(new TripleUsesType(methodNode, rt.ToTypeNode()));
            }
        }

        /// <summary>이 타입이 구현하는 인터페이스 멤버를, 이 타입에서 구현한 메서드와 연결.</summary>
        public static void GetInterfaceImplementations(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
        {
            if (sem.GetDeclaredSymbol(declaration) is not INamedTypeSymbol typeSymbol)
                return;
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (typeSymbol.FindImplementationForInterfaceMember(member) is IMethodSymbol impl
                        && SymbolEqualityComparer.Default.Equals(impl.ContainingType, typeSymbol))
                    {
                        triples.Add(new TripleImplementsMethod(impl.ToMethodNode(), member.ToMethodNode()));
                    }
                }
            }
        }

        private static string GetName(string filePath)
            => filePath.Split(Path.DirectorySeparatorChar).Reverse().FirstOrDefault();

        private static List<TripleIncludedIn> GetFolderChain(string filePath, FileNode file)
        {
            var triples = new List<TripleIncludedIn>();
            var chain = filePath.Split(Path.DirectorySeparatorChar);
            FolderNode prev = null;
            var path = string.Empty;
            foreach (var item in chain)
            {
                if (string.IsNullOrEmpty(path))
                {
                    path = item;
                    prev = new FolderNode(path, item);
                    continue;
                }
                if (item == file.Name)
                {
                    triples.Add(new TripleIncludedIn(file, prev));
                    return triples;
                }
                else
                {
                    path = Path.DirectorySeparatorChar == '/' ? $"{path}/{item}" : $"{path}\\{item}";
                    triples.Add(new TripleIncludedIn(new FolderNode(path, item), new FolderNode(prev.FullName, prev.Name)));
                    prev = new FolderNode(path, item);
                }
            }
            return triples;
        }

        /// <summary>
        /// Entry to analyze class or interface
        /// </summary>
        public static void AnalyzeTree<T>(IList<Triple> triples, SyntaxTree st, SemanticModel sem, FolderNode rootFolder)
            where T : TypeDeclarationSyntax
        {
            var root = st.GetRoot();
            var filePath = root.SyntaxTree.FilePath;
            var index = filePath.IndexOf(rootFolder.Name);
            filePath = index < 0 ? filePath : filePath[index..];
            var fileName = GetName(filePath);
            var fileNode = new FileNode(filePath, fileName);
            GetFolderChain(filePath, fileNode).ForEach(triples.Add);
            var declarations = root.DescendantNodes().OfType<T>();
            foreach (var declaration in declarations)
            {
                var node = sem.GetDeclaredSymbol(declaration).CreateTypeNode(declaration);
                if (node != null)
                {
                    triples.Add(new TripleDeclaredAt(node, fileNode));
                    GetInherits(triples, declaration, sem, node);
                    GetMethodsAll(triples, declaration, sem, node);
                }
            }
        }

        /// <summary>
        /// Member (field, property) initialization
        /// </summary>
        //public static void GetConstructsWithinClass(IList<Triple> triples, ClassDeclarationSyntax declaration, SemanticModel sem, ClassNode classNode)
        //{
        //    var creates = declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
        //    foreach (var creation in creates)
        //    {
        //        var node = sem.GetTypeInfo(creation).CreateClassNode();
        //        triples.Add(new TripleConstruct(classNode, node));
        //    }
        //}

        /// <summary>
        /// Type inherited from BaseType
        /// </summary>
        public static void GetInherits(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem, TypeNode node)
        {
            if (declaration.BaseList != null)
            {
                foreach (var baseTypeSyntax in declaration.BaseList.Types)
                {
                    var parentNode = sem.GetTypeInfo(baseTypeSyntax.Type).CreateTypeNode();
                    if (node is ClassNode classNode)
                    {
                        triples.Add(new TripleOfType(classNode, parentNode));
                    }
                    if (node is InterfaceNode interfaceNode && parentNode is InterfaceNode parentInterfaceNode)
                    {
                        triples.Add(new TripleOfType(interfaceNode, parentInterfaceNode));
                    }
                }
            }
        }

        /// <summary>
        /// Class or Interface have some method AND some method can call another method AND some method can creates an object of class
        /// </summary>
        public static void GetMethodsAll(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem, TypeNode node)
        {
            var methods = declaration.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var methodNode = sem.GetDeclaredSymbol(method).CreateMethodNode(method);
                triples.Add(new TripleHave(node, methodNode));

                foreach (var syntax in method.DescendantNodes().OfType<ExpressionSyntax>())
                {
                    switch (syntax)
                    {
                        case ObjectCreationExpressionSyntax creation:
                            var classNode = sem.GetTypeInfo(creation).CreateClassNode();
                            triples.Add(new TripleConstruct(methodNode, classNode));
                            break;

                        case InvocationExpressionSyntax invocation:
                            if (sem.GetSymbolInfo(invocation).Symbol is IMethodSymbol invokedSymbol)
                            {
                                var invokedMethod = invokedSymbol.CreateMethodNode();
                                triples.Add(new TripleInvoke(methodNode, invokedMethod));
                            }
                            break;
                    }
                }
            }
        }
    }
}