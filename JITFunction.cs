using System;
using System.Linq.Expressions;
using System.Collections.Generic;

namespace Kerbulator {
	public class JITFunction {
		private string id;
		private Dictionary<string, Operator> operators;
		private Dictionary<string, Variable> locals;
		private Dictionary<string, Variable> globals;
		private Dictionary<string, JITFunction> functions;
		private ConstantExpression thisExpression;
		private Queue<Token> tokens;

		List<string> ins;
		List<string> outs;
		List<string> inDescriptions;
		List<string> outDescriptions;

		private bool inError = false;
		private string errorString = "";

		public JITFunction(string id, string expression, Dictionary<string, Operator> operators, Dictionary<string, Variable> globals, Dictionary<string, JITFunction> functions) {
			this.id = id;

			this.ins = new List<string>();
			this.outs = new List<string>();
			this.inDescriptions = new List<string>();
			this.outDescriptions = new List<string>();

			this.operators = operators;
			this.locals = new Dictionary<string, Variable>();
			this.globals = globals;
			this.functions = functions;
			this.thisExpression = Expression.Constant(this);

			try {
				Tokenizer tok = new Tokenizer("unnamed");
				tok.Tokenize(expression);
				tokens = tok.tokens;
			} catch(Exception e) {
				inError = true;
				errorString = e.Message;
			}
		}

		public string Id {
			get { return id; }
			protected set {}
		}

		public List<string> Ins {
			get { return ins; }
			protected set {}
		}

		public List<string> InDescriptions {
			get { return inDescriptions; }
			protected set {}
		}

		public List<string> Outs {
			get { return outs; }
			protected set {}
		}

		public List<string> OutDescriptions {
			get { return outDescriptions; }
			protected set {}
		}

		public bool InError {
			get { return inError; }
			set { inError = value; if(!value) errorString = ""; }
		}

		public string ErrorString {
			get { return errorString; }
			set { errorString = (string)value; Kerbulator.DebugLine(value); inError = true; }
		}

		public void Parse() {
		}

		public Expression ParseStatement() {
			if(tokens.Peek().type == TokenType.END)
				return null;

			List<string> ids = new List<string>();

			while(true) {
				Token t = Consume(TokenType.IDENTIFIER);
				ids.Add(t.val);
				if(tokens.Peek().val == "=")
					break;
				else
					Consume(",");
			}

			Consume("=");
			Expression expr = ParseExpression();

			if(ids.Count == 1) {
				/*
				ParameterExpression result = Expression.Parameter(typeof(double), ids[0]);
				return Expression.Block(
					new[] {result},
					expr
				);
				*/
				return expr;
			} else {
				throw new Exception("Not implemented.");
			}
		}

		public Expression ParseExpression() {
			Stack<Expression> expr = new Stack<Expression>();
			Stack<Operator> ops = new Stack<Operator>();

			bool end = false; // If ever set to true, this is the end of the expression

			while(!end && tokens.Count > 0 && tokens.Peek().type != TokenType.END) {
				Token t = tokens.Peek();
				Kerbulator.DebugLine("Token: "+ Enum.GetName(typeof(TokenType), t.type) +": "+ t.val);

				switch(t.type) {
					case TokenType.NUMBER:
						ParseNumber(expr);
						break;
					case TokenType.OPERATOR:
						ParseOperator(expr, ops);
						break;
					case TokenType.BRACE:
						end = ParseBrace(expr, ops);
						break;
					case TokenType.IDENTIFIER:
						ParseIdentifier(expr, ops);
						break;
					case TokenType.COMMA:
						end = true;
						break;
					default:
						Consume();
						break;
				}

			}

			// Handle remaining ops
			while(ops.Count > 0) {
				Operator op = ops.Pop();
				expr.Push( ExecuteOperator(op, expr, ops) );
			}

			if(expr.Count > 1)
				throw new Exception("Malformed expression");

			return expr.Pop();
		}

		private Token Consume() {
			if(tokens.Count == 0)
				throw new Exception("Unexpected end of expression.");

			return tokens.Dequeue();
		}

		private Token Consume(string val) {
			if(tokens.Count == 0)
				throw new Exception("Expected: "+ val);

			Token t = tokens.Dequeue();
			if(!String.Equals(t.val, val))
				throw new Exception("Expected: "+ val);

			return t;
		}

		private Token Consume(TokenType type) {
			if(tokens.Count == 0)
				throw new Exception("Expected "+ Enum.GetName(typeof(TokenType), type));

			Token t = tokens.Dequeue();
			if(t.type != type)
				throw new Exception("Expected "+ Enum.GetName(typeof(TokenType), type));

			return t;
		}

		private bool PossiblyValidExpression(Stack<Expression>expr, Stack<Operator> ops) {
			if(expr.Count == 0 && ops.Count == 0)
				return false;

			int required = 0;
			int supplied = expr.Count;

			foreach(Operator op in ops) {
				Kerbulator.DebugLine(op.id);
				supplied ++;
				if(op.arity == Arity.BINARY)
					required += 2;
				else
					required += 1;
			}

			Kerbulator.DebugLine("required: "+ required +", supplied: "+ supplied);
			return supplied == required + 1;
		}

		private void ParseNumber(Stack<Expression> expr) {
			Token t = tokens.Dequeue();
			Kerbulator.DebugLine("Pushing "+ t.val);
			/*
			expr.Push(
				Expression.Convert(
					Expression.Constant(Double.Parse(t.val, System.Globalization.CultureInfo.InvariantCulture)),
					typeof(Object)
				)
			);
			*/
			expr.Push(
				Expression.Constant(Double.Parse(t.val, System.Globalization.CultureInfo.InvariantCulture))
			);
		}

		private void ParseOperator(Stack<Expression>expr, Stack<Operator>ops) {
			Token t = tokens.Dequeue();
			Operator op = operators[t.val];

			// Handle ambiguous cases of arity
			if(op.arity == Arity.BOTH) {	
				if(PossiblyValidExpression(expr, ops) ) {
					op = new Operator(op.id, op.precidence, Arity.BINARY);
					Kerbulator.DebugLine(op.id +" is binary.");
				} else {
					op = new Operator(op.id, 3, Arity.UNARY);
					Kerbulator.DebugLine(op.id +" is unary.");
				}
			} 

			// Handle operators with higher precidence
			while(ops.Count > 0) {
				Operator prevOp = ops.Peek();

				if(op.arity != Arity.BINARY || prevOp.precidence < op.precidence)
					// Leave for later
					break;
				else
					expr.Push( ExecuteOperator(ops.Pop(), expr, ops) );
			}

			// Push current operator on the stack
			Kerbulator.DebugLine("Pushing "+ op.id);
			ops.Push(op);
		}

		private Expression ExecuteOperator(Operator op, Stack<Expression> expr, Stack<Operator> ops) {
			Kerbulator.DebugLine("Executing: "+ op.id);
			if(op.arity == Arity.BINARY && expr.Count < 2)
				throw new Exception("Operator "+ op.id +" expects both a left and a right hand side to operate on.");
			else if(op.arity == Arity.UNARY && expr.Count < 1)
				throw new Exception("Operator "+ op.id +" expects a right hand side to operate on.");
			else if(op.arity == Arity.BOTH)
				throw new Exception("Arity of "+ op.id +" still undefined.");

			Expression a,b;
			switch(op.id) {
				case "+":
					b = expr.Pop(); a = expr.Pop();
					return Expression.Add(a, b);
				case "-":
					b = expr.Pop();
					if(op.arity == Arity.UNARY) 
						return Expression.Negate(b);
					else {
						a = expr.Pop();
						return Expression.Subtract(a, b);
					}
				case "*":
				case "·":
					b = expr.Pop(); a = expr.Pop();
					return Expression.Multiply(a, b);
				case "/":
				case "÷":
					b = expr.Pop(); a = expr.Pop();
					return Expression.Divide(a, b);
				case "%":
					b = expr.Pop(); a = expr.Pop();
					return Expression.Modulo(a, b);
				case "^":
					b = expr.Pop(); a = expr.Pop();
					return Expression.Power(a, b);
				case "√":
					b = expr.Pop();
					if(op.arity == Arity.UNARY) {
						return Expression.Call(
							typeof(Math).GetMethod("Sqrt"),
						   	b
						);
					} else {
						a = expr.Pop();
						return Expression.Power(
							a,
							Expression.Divide(Expression.Constant(1.0), b)
						);
					}
				case "⌊":
					b = expr.Pop();
					return Expression.Call(
						typeof(Math).GetMethod("Floor", new[] {typeof(double)}),
						b
					);
				case "⌈":
					b = expr.Pop();
					return Expression.Call(
						typeof(Math).GetMethod("Ceiling", new[] {typeof(double)}),
						b
					);
				case "|":
					b = expr.Pop();
					return Expression.Call(
						typeof(Math).GetMethod("Abs", new[] {typeof(double)}),
						b
					);

				case "buildin-function":
					List<Expression> args = new List<Expression>();
					args.Add(expr.Pop());
					a = expr.Pop();
					return ParseBuildInFunction(globals[(string)((ConstantExpression)a).Value], args);

				case "user-function":
					List<Expression> args2 = new List<Expression>();
					args2.Add(expr.Pop());
					a = expr.Pop();
					throw new Exception("User functions not implemented yet.");
					// return ParseUserFunction(functions[(string)((ConstantExpression)a).Value], args2);

				default:
					throw new Exception("Unknown operator: "+ op.id);
			}
		}

		private bool ParseBrace(Stack<Expression> expr, Stack<Operator> ops) {
			Token t = tokens.Peek();

			// Determine whether it's a left or right brace
			bool isLeft = false;
			switch(t.val) {
				case "(":
				case "{":
				case "⌊":
				case "⌈":
					isLeft = true;
					break;
				case "|":
					isLeft = !PossiblyValidExpression(expr, ops);

					if(isLeft)
						Kerbulator.DebugLine("| is left brace");
					else {
						Kerbulator.DebugLine("| is right brace");
					}
					break;
			} 

			// If it's a left brace, start a sub-expression
			if(isLeft) {
				Consume();

				// Execute sub-expression
				Kerbulator.DebugLine("Starting subexpression");
				Expression subexpr = ParseExpression();
				Kerbulator.DebugLine("End of subexpression");
				expr.Push(subexpr);

				// Consume right brace. Execute operation if any
				switch(t.val) {
					case "(":
						Consume(")");
						break;
					case "{":
						Consume("}");
						break;
					case "⌊":
						Consume("⌋");
						ops.Push(operators[t.val]);
						break;
					case "⌈":
						Consume("⌉");
						ops.Push(operators[t.val]);
						break;
					case "|":
						Consume("|");
						ops.Push(operators[t.val]);
						break;
				}
				return false;
			} else { 
				return true;
			}
		}

		public double GetLocal(string id) {
			if(!locals.ContainsKey(id))
				throw new Exception("In function "+ this.id +": variable "+ id +" is not defined.");
			return locals[id].val;
		}

		public double GetGlobal(string id) {
			if(!locals.ContainsKey(id))
				throw new Exception("In function "+ this.id +": variable "+ id +" is not defined.");
			return globals[id].val;
		}

		public JITFunction GetFunction(string id) {
			if(!locals.ContainsKey(id))
				throw new Exception("In function "+ this.id +": function "+ id +" is not defined.");
			return functions[id];
		}

		private void ParseIdentifier(Stack<Expression> expr, Stack<Operator> ops) {
			Token t = tokens.Dequeue();

			if(functions.ContainsKey(t.val)) {
				// User function call
				JITFunction f = functions[t.val];
				if(tokens.Count > 0 && tokens.Peek().val == "(") {
					// Parameter list supplied, execute function now
					List<Expression> args = ParseArgumentList();
					if(args.Count != f.Ins.Count)
						throw new Exception(t.pos + "function "+ f.Id +" takes "+ f.Ins.Count +" arguments, but "+ args.Count +" were supplied");
					// expr.Push( ParseUserFunction(f, args) );
				} else if(f.Ins.Count == 0) {
					// Function takes no arguments, execute now
					// expr.Push( ParseUserFunction(f, new List<Expression>()) );
				} else {
					// Do function call later, when parameters are known
					ops.Push(operators["user-function"]);
					expr.Push(Expression.Constant(t.val));
				}

			} else if(globals.ContainsKey(t.val)) {
				// Global identifier
				Variable var = globals[t.val];
				switch(var.type) {
					case VarType.FUNCTION:
						if(tokens.Count > 0 && tokens.Peek().val == "(") {
							// Parameter list supplied, execute function now
							List<Expression> args = ParseArgumentList();
							if(args.Count != var.numArgs)
								throw new Exception(t.pos + "function "+ var.id +" takes "+ var.numArgs +" arguments, but "+ args.Count +" were supplied");
							expr.Push( ParseBuildInFunction(var, args) );
						} else if(var.numArgs == 0) {
							// Function takes no arguments, execute now
							expr.Push( ParseBuildInFunction(var, new List<Expression>()) );
						} else {
							// Do function call later, when parameters are known
							ops.Push(operators["buildin-function"]);
							expr.Push(Expression.Constant(t.val));
						}
						break;

					case VarType.NUMBER:
						expr.Push(Expression.Constant(var.val));
						break;

					default:
						throw new Exception(t.pos +" variable type not implemented.");
				}

			} else {
				// Local identifier
				expr.Push(
					Expression.Call(
						thisExpression,
						typeof(JITFunction).GetMethod("GetLocal"),
						Expression.Constant(t.val)
					)
				);	
			}
		}

		private List<Expression> ParseArgumentList() {
			List<Expression> arguments = new List<Expression>();

			Consume("(");

			while(tokens.Peek().val != ")") {
				Expression subexpr = ParseExpression();
				arguments.Add(subexpr);
				
				if(tokens.Peek().val != ")")
					Consume(TokenType.COMMA);
			}

			Consume(")");

			return arguments;
		}

		private Expression ParseBuildInFunction(Variable func, List<Expression> arguments) {
			switch(func.id) {
				case "abs":
					return Expression.Call(
						typeof(Math).GetMethod("Abs", new[] {typeof(double)}),
						arguments[0]
					);
				case "sin":
					return Expression.Call(
						typeof(Math).GetMethod("Sin", new[] {typeof(double)}),
						arguments[0]
					);
				default:
					throw new Exception("Unknown build-in function: "+ func.id);
			}
		}

		/*
		private Expression ParseUserFunction(JITFunction func, List<Expression> arguments) {
			return Expression.Call(
				Expression.Constant(func),
				typeof(JITFunction).GetMethod("Execute"),
				arguments
			);
		}
		*/
	}
}
