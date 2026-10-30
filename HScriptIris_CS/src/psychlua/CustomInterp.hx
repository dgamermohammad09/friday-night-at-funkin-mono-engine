package psychlua;

/**
 * Psych Engine 1.0.4 `psychlua.HScript.CustomInterp` (source/psychlua/HScript.hx), compiled to C#
 * together with hscript-iris. Unknown identifiers resolve against `parentInstance` (the PlayState,
 * i.e. the MonoGame PlayScene), so scripts can use `curBeat`, `boyfriend`, `add(...)` directly.
 *
 * Only change from Psych: the instance-field check is case-insensitive, because the C# engine
 * exposes PascalCase members (CurBeat, Boyfriend) where the Haxe original has camelCase.
 */
class CustomInterp extends crowplexus.hscript.Interp
{
	public var parentInstance(default, set):Dynamic = [];
	private var _instanceFields:Array<String>;
	function set_parentInstance(inst:Dynamic):Dynamic
	{
		parentInstance = inst;
		if(parentInstance == null)
		{
			_instanceFields = [];
			return inst;
		}
		_instanceFields = [for (f in Type.getInstanceFields(Type.getClass(inst))) f.toLowerCase()];
		return inst;
	}

	public function new()
	{
		super();
	}

	override function fcall(o:Dynamic, funcToRun:String, args:Array<Dynamic>):Dynamic {
		for (_using in usings) {
			var v = _using.call(o, funcToRun, args);
			if (v != null)
				return v;
		}

		var f = get(o, funcToRun);

		if (f == null) {
			crowplexus.iris.Iris.error('Tried to call null function $funcToRun', posInfos());
			return null;
		}

		return Reflect.callMethod(o, f, args);
	}

	override function resolve(id: String): Dynamic {
		if (locals.exists(id)) {
			var l = locals.get(id);
			return l.r;
		}

		if (variables.exists(id)) {
			var v = variables.get(id);
			return v;
		}

		if (imports.exists(id)) {
			var v = imports.get(id);
			return v;
		}

		// C# target: Type.getInstanceFields() omits C# properties (PlayScene.CurBeat), which are plain
		// vars in the Haxe original, so also accept anything Reflect can see on the instance.
		if(parentInstance != null && (_instanceFields.contains(id.toLowerCase()) || Reflect.hasField(parentInstance, id))) {
			var v = Reflect.getProperty(parentInstance, id);
			return v;
		}

		error(EUnknownVariable(id));

		return null;
	}
}
