// Entry point that keeps Psych Engine's HScript stack (hscript-iris 1.1.3) for C# generation.
import crowplexus.iris.Iris;
import crowplexus.hscript.Parser;
import crowplexus.hscript.Interp;
import crowplexus.hscript.Expr;
import psychlua.CustomInterp;
class IrisEntry {
  public static function keep():Void {
    var p = new Parser(); var i = new Interp();
    var x = new Iris("trace(1);"); var c = new CustomInterp();
  }
  static function main() {}
}
