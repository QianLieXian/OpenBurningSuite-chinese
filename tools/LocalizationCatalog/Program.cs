using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
var root = Path.Combine(Path.GetFullPath(args.FirstOrDefault() ?? "."), "OpenBurningSuite");
var entries = new List<object>();
foreach(var file in Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).Where(f => !Path.GetRelativePath(root, f).Replace('\\', '/').Split('/').Any(part => part is "obj" or "bin" or "Localization"))) {
 var syntax=CSharpSyntaxTree.ParseText(File.ReadAllText(file));
 foreach(var node in syntax.GetRoot().DescendantNodes()) {
  string value=null;
  if(node is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)) value=lit.Token.ValueText;
  if(node is InterpolatedStringExpressionSyntax interp) {
   int i=0;
   value=string.Concat(interp.Contents.Select(c=>c is InterpolatedStringTextSyntax t?t.TextToken.ValueText: $"{{{i++}}}"));
  }
  if(node is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression)
     && binary.Parent is not BinaryExpressionSyntax && HasText(binary)) {
   int slot=0;
   value=Template(binary,ref slot);
  }
  if(value!=null) entries.Add(new{file=Path.GetRelativePath(root,file),line=syntax.GetLineSpan(node.Span).StartLinePosition.Line+1,value,kind=node.Kind().ToString()});
 }
}
Console.Write(JsonSerializer.Serialize(entries));

static bool HasText(ExpressionSyntax expression) => expression switch {
 LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
 InterpolatedStringExpressionSyntax => true,
 BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) => HasText(binary.Left)||HasText(binary.Right),
 ParenthesizedExpressionSyntax group => HasText(group.Expression),
 _ => false
};
static string Template(ExpressionSyntax expression, ref int slot) {
 if(expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) return literal.Token.ValueText;
 if(expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression)) return Template(binary.Left,ref slot)+Template(binary.Right,ref slot);
 if(expression is ParenthesizedExpressionSyntax group && HasText(group.Expression)) return Template(group.Expression,ref slot);
 if(expression is InterpolatedStringExpressionSyntax interpolation) {
  var parts=new List<string>();
  foreach(var part in interpolation.Contents)
   parts.Add(part is InterpolatedStringTextSyntax text?text.TextToken.ValueText:$"{{{slot++}}}");
  return string.Concat(parts);
 }
 return $"{{{slot++}}}";
}
