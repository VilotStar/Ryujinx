using Ryujinx.Graphics.Shader.Decoders;
using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.Translation;
using System.Collections.Generic;
using System.Linq;

using static Ryujinx.Graphics.Shader.IntermediateRepresentation.OperandHelper;

namespace Ryujinx.Graphics.Shader.Instructions
{
    static partial class InstEmit
    {
        public static void Bra(EmitterContext context)
        {
            EmitBranch(context, context.CurrBlock.Branch.Address);
        }

        public static void Brk(EmitterContext context)
        {
            EmitBrkOrSync(context);
        }

        public static void Brx(EmitterContext context)
        {
            OpCodeBranchIndir op = (OpCodeBranchIndir)context.CurrOp;

            int offset = (int)op.Address + 8 + op.Offset;

            Operand address = context.IAdd(Register(op.Ra), Const(offset));

            // Sorting the target addresses in descending order improves the code,
            // since it will always check the most distant targets first, then the
            // near ones. This can be easily transformed into if/else statements.
            IOrderedEnumerable<Block> sortedTargets = op.PossibleTargets.OrderByDescending(x => x.Address);

            Block lastTarget = sortedTargets.LastOrDefault();

            foreach (Block possibleTarget in sortedTargets)
            {
                Operand label = context.GetLabel(possibleTarget.Address);

                if (possibleTarget != lastTarget)
                {
                    context.BranchIfTrue(label, context.ICompareEqual(address, Const((int)possibleTarget.Address)));
                }
                else
                {
                    context.Branch(label);
                }
            }
        }

        public static void Exit(EmitterContext context)
        {
            OpCodeExit op = (OpCodeExit)context.CurrOp;

            // TODO: Figure out how this is supposed to work in the
            // presence of other condition codes.
            if (op.Condition == Condition.Always)
            {
                context.Return();
            }
        }

        public static void Kil(EmitterContext context)
        {
            context.Discard();
        }

        public static void Pbk(EmitterContext context)
        {
            EmitPbkOrSsy(context);
        }

        public static void Ssy(EmitterContext context)
        {
            EmitPbkOrSsy(context);
        }

        public static void Sync(EmitterContext context)
        {
            EmitBrkOrSync(context);
        }

        private static void EmitPbkOrSsy(EmitterContext context)
        {
            OpCodePush op = (OpCodePush)context.CurrOp;

            foreach (KeyValuePair<OpCodeBranchPop, Operand> kv in op.PopOps)
            {
                OpCodeBranchPop opSync = kv.Key;

                Operand local = kv.Value;

                int pushOpIndex = opSync.Targets[op];

                context.Copy(local, Const(pushOpIndex));
            }
        }

        private static void EmitBrkOrSync(EmitterContext context)
        {
            OpCodeBranchPop op = (OpCodeBranchPop)context.CurrOp;

            if (op.Targets.Count == 1)
            {
                // If we have only one target, then the SSY/PBK is basically
                // a branch, we can produce better codegen for this case.
                OpCodePush pushOp = op.Targets.Keys.First();

                EmitBranch(context, pushOp.GetAbsoluteAddress());
            }
            else
            {
                foreach (KeyValuePair<OpCodePush, int> kv in op.Targets)
                {
                    OpCodePush pushOp = kv.Key;

                    Operand label = context.GetLabel(pushOp.GetAbsoluteAddress());

                    Operand local = pushOp.PopOps[op];

                    int pushOpIndex = kv.Value;

                    context.BranchIfTrue(label, context.ICompareEqual(local, Const(pushOpIndex)));
                }
            }
        }

        private static void EmitBranch(EmitterContext context, ulong address)
        {
            // If we're branching to the next instruction, then the branch
            // is useless and we can ignore it.
            if (address == context.CurrOp.Address + 8)
            {
                return;
            }

            Operand label = context.GetLabel(address);

            Operand pred = Register(context.CurrOp.Predicate);

            if (context.CurrOp.Predicate.IsPT)
            {
                context.Branch(label);
            }
            else if (context.CurrOp.InvertPredicate)
            {
                context.BranchIfFalse(label, pred);
            }
            else
            {
                context.BranchIfTrue(label, pred);
            }
        }
    }
}