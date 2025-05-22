using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Globalization;
using System.Drawing;

namespace WPProduction.Utils
{
    /// <summary>
    /// Yeah there's CSV classes and NewtonSoft's JSON.Net has a JsonWriter but they're all lame.
    /// This is easy and I control it.
    /// </summary>
    /// <todo>
    /// - add writePair [string], [int], [bool] etc
    /// </todo>
    public abstract class ThingWriter
    {
        protected int lvl = 0;
        protected int chartCount = 0;

        public ThingWriter() { }

        abstract public void startBlock(string name);
        abstract public void startArray(string name);
        abstract public void startObject();

        abstract public void writePair(string name, string value);
        abstract public void writePair(string name, string[] values);
        abstract public void writePair(string name, bool value);
        //for nullable bools, if the value does not exist it is not written at all, see both JSON and CSV writers
        abstract public void writePair(string name, bool? value);
        abstract public void writePair(string name, int value);
        abstract public void writePair(string name, uint value);
        abstract public void writePair(string name, int[] values);
        abstract public void writePair(string name, DateTime value);
        abstract public void write(string name, int repeats = 1);
        abstract public void write(int[] values);

        abstract public void writePair(string name, double value, int? prec = 2);
        abstract public void writePair(string name, double[] values, int? prec = 2);
        abstract public void writePair(string name, float[] values, int? prec = 2);
        abstract public void writePair(string name, ushort[] values);
        abstract public void writePair(string name, short[] values);
        abstract public void write(double[] values, int? prec = 2);
        abstract public void write(float[] values, int? prec = 2);
        abstract public void write(ushort[] values);
        abstract public void write(short[] values);

        abstract public void closeArray();
        abstract public void closeBlock();
        abstract public void closeObject();

        public void blank() => write(Environment.NewLine);
    }

    /// <summary>
    /// Mark's Jason Serializer
    /// </summary>
    ///
    /// <remarks>
    /// I looked at Newtonsoft.Json.JsonTextWriter, and it doesn't seem to do much.
    /// 
    /// This is based on a StringBuilder rather than StringWriter or StreamWriter 
    /// so it can "back up" and remove those otherwise highly-convenient trailing
    /// commas.
    /// 
    /// All startFoo() methods append at least one trailing whitespace, so 
    /// closeFoo() can remove it for empty blocks.
    ///
    /// All writePair() methods append trailing comma, which closeFoo() removes.
    /// </remarks>
    public class JsonThingWriter : ThingWriter
    {
        StringBuilder sb;
        SortedSet<char> ignore = new SortedSet<char>();
 
        public JsonThingWriter() : base()
        {
            sb = new StringBuilder();

            ignore.Add(' ');
            ignore.Add(',');
            ignore.Add('\n');
        }

        public override string ToString()
        {
            backupToContent();
            return "{\n" + sb.ToString() + "\n}";
        }

        public void startBlock(uint number) { sb.AppendFormat("{0}{1}: {{\n", indent, number); lvl++; }
        override public void startBlock(string name) { sb.AppendFormat("{0}\"{1}\": {{\n" , indent, name); lvl++; }
        override public void startObject() { sb.AppendFormat("{0}{{\n", indent); lvl++; }
        override public void startArray(string name) { sb.AppendFormat("{0}\"{1}\": [ " , indent, name); lvl++; }

        override public void writePair(string name, string value) { sb.AppendFormat("{0}\"{1}\": \"{2}\",\n", indent, name, value); }
        override public void writePair(string name, string[] values) 
        { 
            if (values.Length > 0)
                sb.AppendFormat("{0}\"{1}\": [ \"{2}\" ],\n", indent, name, string.Join("\", \"", values));
            else
                sb.AppendFormat("{0}\"{1}\": [ ],\n", indent, name);
        }
        override public void writePair(string name, bool value) { sb.AppendFormat("{0}\"{1}\": {2},\n", indent, name, value ? "true" : "false"); }
        override public void writePair(string name, bool? value) 
        { 
            if (value.HasValue)
                sb.AppendFormat("{0}\"{1}\": {2},\n", indent, name, value.Value ? "true" : "false"); 
        }
        override public void writePair(string name, int value) { sb.AppendFormat("{0}\"{1}\": {2},\n", indent, name, value); }
        override public void writePair(string name, uint value) { sb.AppendFormat("{0}\"{1}\": {2},\n", indent, name, value); }
        override public void writePair(string name, int[] values) { sb.AppendFormat("{0}\"{1}\": [ {2} ],\n", indent, name, string.Join(", ", values)); }
        override public void writePair(string name, DateTime value) { sb.AppendFormat("{0}\"{1}\": \"{2}\",\n", indent, name, value.ToString("s")); }
        override public void write(int[] values) { sb.AppendFormat("[ {0} ],\n", string.Join(", ", values)); }
        override public void write(string text, int repeats = 1) { sb.Append(text); }

        override public void closeArray()
        {
            backupToContent();
            lvl--;
            sb.AppendFormat("\n{0}", indent);
            sb.Append("]");
            sb.Append(",");
            sb.Append("\n");
        }

        override public void closeBlock()
        {
            backupToContent();
            lvl--;
            sb.AppendFormat("\n{0}}},\n", indent);
        }

        override public void closeObject() => closeBlock();

        ////////////////////////////////////////////////////////////////////////
        // Utils
        ////////////////////////////////////////////////////////////////////////

        string indent => new string(' ', lvl * 2);

        public void backupToContent()
        {
            while (sb.Length > 0 && ignore.Contains(sb[sb.Length - 1]))
                sb.Remove(sb.Length - 1, 1);
        }

        ////////////////////////////////////////////////////////////////////////
        // Floating Point
        ////////////////////////////////////////////////////////////////////////
        public void writePair(double name, double value, int? prec = 2)
        {
            if (prec == null)
                sb.AppendFormat("{0}\"{1}\": {2},\n", indent, name, value);
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                sb.AppendFormat(format, "{0}{1:F}: {2:F},\n", indent, name, value);
            }
        }

        override public void writePair(string name, double value, int? prec = 2)
        {
            if (prec == null)
                sb.AppendFormat("{0}\"{1}\": {2},\n", indent, name, value);
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                sb.AppendFormat(format, "{0}\"{1}\": {2:F},\n", indent, name, value);
            }
        }

        public override void writePair(string name, double[] values, int? prec = 2)
        {
            if (prec == null)
            {
                sb.AppendFormat("{0}\"{1}\": ", indent, name);
                write(values, prec);
            }
            else
            {
                sb.AppendFormat("{0}\"{1}\": [", indent, name);
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                foreach (double v in values)
                    sb.AppendFormat(format, " {0:F},", v);
                backupToContent();
                sb.Append(" ],\n");
            }
        }

        public override void writePair(string name, float[] values, int? prec = 2)
        {
            if (prec == null)
            {
                sb.AppendFormat("{0}\"{1}\": ", indent, name);
                write(values, prec);
            }
            else
            {
                sb.AppendFormat("{0}\"{1}\": [", indent, name);
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                foreach (double v in values)
                    sb.AppendFormat(format, " {0:F},", v);
                backupToContent();
                sb.Append(" ],\n");
            }
        }

        public void writePair(string name, uint[] values)
        {
            sb.AppendFormat("{0}\"{1}\": ", indent, name);
            write(values);
        }

        public override void writePair(string name, ushort[] values)
        {
            sb.AppendFormat("{0}\"{1}\": ", indent, name);
            write(values);
        }


        public override void writePair(string name, short[] values)
        {
            sb.AppendFormat("{0}\"{1}\": ", indent, name);
            write(values);
        }

        // this is for anonymous internal 2D arrays like "foo": [ 
        //     [ 1, 2, 3 ], 
        //     [ 4, 5, 6 ] 
        // ]
        override public void write(double[] values, int? prec = 2)
        {
            sb.Append("[");

            if (prec == null)
                sb.Append(" " + string.Join(", ", values));
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                foreach (double v in values)
                    sb.AppendFormat(format, " {0:F},", v);
                backupToContent();
            }

            sb.Append(" ],\n");
        }

        override public void write(float[] values, int? prec = 2)
        {
            sb.Append("[");

            if (prec == null)
                sb.Append(" " + string.Join(", ", values));
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                foreach (float v in values)
                    sb.AppendFormat(format, " {0:F},", v);
                backupToContent();
            }

            sb.Append(" ],\n");
        }

        public void write(uint[] values)
        {
            sb.Append("[");
            sb.Append(" " + string.Join(", ", values));

            sb.Append(" ],\n");
        }

        override public void write(ushort[] values)
        {
            sb.Append("[");
            sb.Append(" " + string.Join(", ", values));
            
            sb.Append(" ],\n");
        }

        override public void write(short[] values)
        {
            sb.Append("[");
            sb.Append(" " + string.Join(", ", values));

            sb.Append(" ],\n");
        }
    }

    public class CSVThingWriter : ThingWriter
    {
        StreamWriter outfile;

        public CSVThingWriter(StreamWriter outfile) : base() { this.outfile = outfile; }
        // string indent { get { return new string (',', lvl); } }
        string indent => "";

        override public void startBlock(string name) { outfile.WriteLine("{0}{1}[{2}]", Environment.NewLine, indent, name); lvl++; } 
        override public void startObject() { outfile.WriteLine("{0}{1}", Environment.NewLine, indent); lvl++; } 
        override public void startArray(string name) { outfile.Write("{0}{1},", indent, name); lvl++; }

        override public void writePair(string name, string value) { outfile.WriteLine("{0}{1}, {2}", indent, name, value); }
        override public void writePair(string name, string[] values) { writePair(name, values); }
        public void writePair(string name, string[] values, bool blockedFormat = true)
        {
            if (blockedFormat)
            {
                startBlock(name);
                foreach (string s in values) outfile.WriteLine(s);
                closeBlock();
            }
            else
            {
                outfile.WriteLine("{0}{1}, {2}", indent, name, string.Join(", ", values));
            }
        }
        override public void writePair(string name, bool value) { outfile.WriteLine("{0}{1}, {2}", indent, name, value); }
        override public void writePair(string name, bool? value) 
        { 
            if (value.HasValue)
                outfile.WriteLine("{0}{1}, {2}", indent, name, value.Value); 
        }
        override public void writePair(string name, int value) { outfile.WriteLine("{0}{1}, {2}", indent, name, value); }
        override public void writePair(string name, uint value) { outfile.WriteLine("{0}{1}, {2}", indent, name, value); }
        override public void writePair(string name, DateTime value) { outfile.WriteLine("{0}{1}, {2}", indent, name, value); }
        override public void writePair(string name, int[] values) { outfile.WriteLine("{0}{1}, {2}", indent, name, string.Join(", ", values)); }
        public override void write(int[] values) { outfile.Write(string.Join(", ", values)); }
        override public void write(string text, int repeats = 1) 
        {
            text = text.Replace("\n", Environment.NewLine);

            for (int i = 0; i < repeats; ++i)
                outfile.Write(text); 
        }

        override public void closeBlock() { /*outfile.WriteLine();*/ lvl--; } 
        override public void closeArray() { /*outfile.WriteLine();*/ lvl--; }
        override public void closeObject() { /*outfile.WriteLine();*/ lvl--; }

        ////////////////////////////////////////////////////////////////////////
        // Floating Point
        ////////////////////////////////////////////////////////////////////////

        override public void writePair(string name, double value, int? prec = 2)
        {
            if (prec == null)
                outfile.WriteLine("{0}{1}, {2}", indent, name, value);
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                outfile.WriteLine(string.Format(format, "{0}{1}, {2:F}", indent, name, value));
            }
        }

        override public void writePair(string name, double[] values, int? prec = 2)
        {
            if (prec == null)
                outfile.WriteLine("{0}{1}, {2}", indent, name, string.Join(", ", values));
            else
            {
                // TODO: change to {2:F}
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                outfile.Write("{0}{1}, ", indent, name);
                for (int i = 0; i < values.Length; i++)
                {
                    outfile.Write(string.Format(format, "{0:F}", values[i]));
                    if (i + 1 < values.Length)
                        outfile.Write(", ");
                }
                outfile.WriteLine();
            }
        }

        override public void writePair(string name, float[] values, int? prec = 2)
        {
            if (prec == null)
                outfile.WriteLine("{0}{1}, {2}", indent, name, string.Join(", ", values));
            else
            {
                // TODO: change to {2:F}
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                outfile.Write("{0}{1}, ", indent, name);
                for (int i = 0; i < values.Length; i++)
                {
                    outfile.Write(string.Format(format, "{0:F}", values[i]));
                    if (i + 1 < values.Length)
                        outfile.Write(", ");
                }
                outfile.WriteLine();
            }
        }

        override public void writePair(string name, ushort[] values)
        {
            outfile.WriteLine("{0}{1}, {2}", indent, name, string.Join(", ", values));
        }

        override public void writePair(string name, short[] values)
        {
            outfile.WriteLine("{0}{1}, {2}", indent, name, string.Join(", ", values));
        }
        // this is for anonymous internal 2D arrays like "foo": [ 
        //     [ 1, 2, 3 ], 
        //     [ 4, 5, 6 ] 
        // ]
        override public void write(double[] values, int? prec = 2)
        {
            if (prec == null)
                outfile.Write(string.Join(", ", values));
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                for (int i = 0; i < values.Length; i++)
                {
                    outfile.Write(string.Format(format, "{0:F}", values[i]));
                    if (i + 1 < values.Length)
                        outfile.Write(", ");
                }
            }
        }

        override public void write(float[] values, int? prec = 2)
        {
            if (prec == null)
                outfile.Write(string.Join(", ", values));
            else
            {
                NumberFormatInfo format = new NumberFormatInfo() { NumberDecimalDigits = (int)prec };
                for (int i = 0; i < values.Length; i++)
                {
                    outfile.Write(string.Format(format, "{0:F}", values[i]));
                    if (i + 1 < values.Length)
                        outfile.Write(", ");
                }
            }
        }

        override public void write(ushort[] values)
        {
            outfile.Write(string.Join(", ", values));
        }

        override public void write(short[] values)
        {
            outfile.Write(string.Join(", ", values));
        }

    }

}
