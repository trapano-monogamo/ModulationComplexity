using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Runtime.CompilerServices;
using System.Windows;
using System.IO;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
// [assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /*
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                using (Application app = Application.CreateApplication())
                {
                    Execute(app);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
            Console.Read();
        }

        static void Execute(Application app) { }
    }
    */

    public class Script
    {
        private double[] leafThicknessesInMM;
        private int leafPairCount;

        private string outputpath = @"C:\Modulation\";

        public Script() {
            this.leafPairCount = 60;
            this.leafThicknessesInMM = new double[] { };
        }
        
        public bool checkSignOfPosition(List<float> ps)
        {
            foreach (var p in ps)
            {
                if (p < 0) return true;
            }
            return false;
        }

        /* getAreaUnion:
         * - store signed maximum and minimum leaf posotions on both sides
         * - pair max A with max B => distance is sum of values
         * - multiply distance by leaf width (per pair)
         * */
        public double getAreaUnion(Beam beam) {
            double area = 0.0;
            List<double> max_positions_A = Enumerable.Repeat(-200.0, this.leafThicknessesInMM.Length).ToList();
            List<double> min_positions_B = Enumerable.Repeat(+200.0, this.leafThicknessesInMM.Length).ToList();
            for (int i = 0; i < beam.ControlPoints.Count; i++) {
                for (int j = 0; j < this.leafThicknessesInMM.Length; j++)
                {
                    double aLeaf = beam.ControlPoints[i].LeafPositions[1, j];
                    double bLeaf = beam.ControlPoints[i].LeafPositions[0, j];

                    //MessageBox.Show(string.Format("[{2},{3}] - a: {0}, b: {1}", aLeaf, bLeaf, beam.Id, i));

                    max_positions_A[j] = (aLeaf > max_positions_A[j]) ? aLeaf : max_positions_A[j];
                    min_positions_B[j] = (bLeaf < min_positions_B[j]) ? bLeaf : min_positions_B[j];
                }
            }

            for (int i = 0; i < max_positions_A.Count; i++)
            {
                double aLeaf = max_positions_A[i];
                double bLeaf = min_positions_B[i];

                if (aLeaf - bLeaf <= beam.MLC.MinDoseDynamicLeafGap) continue;

                area += ((aLeaf - bLeaf) * this.leafThicknessesInMM[i]);
            }

            // MessageBox.Show(string.Format("areaunion: {2}\nmax_a: {0}\nmin_b: {1}",
            //     String.Join(", ", max_positions_A.ToArray()),
            //     String.Join(", ", min_positions_B.ToArray()),
            //     area));

            return area;
        }

        public double getArea(ControlPoint cp)
        {
            double retval = 0.0;
            for (int i = 0; i < this.leafPairCount; i++)
            {
                double aLeaf = cp.LeafPositions[1, i];
                double bLeaf = cp.LeafPositions[0, i];
                retval += this.leafThicknessesInMM[i] * (aLeaf - bLeaf);
            }
            return retval;
        }

        // accumulate perimeter leaf be leaf as difference in position.
        // If the areas delimited by bank leaves are separated, then the variation in perimeter is the minimum between
        // the difference in adjacent leaves positions (Pn - Pn+1) and the distance between left and right bank leaves (Pn,a - Pn,b).
        public double getPerimeter(ControlPoint cp, MLC mlc) {
            double perimeter = 0.0;

            // for every leaf
            for (int n = 0; n < this.leafPairCount; n++)
            {
                double an = cp.LeafPositions[1, n]; // a[n]
                double bn = cp.LeafPositions[0, n]; // b[n]

                // if it's the first leaf pair
                if (n==0 && an - bn > mlc.MinDoseDynamicLeafGap)
                {
                    perimeter += Math.Abs(an - bn);
                }

                // if it's the last leaf pair, don't calculate normally
                if (n == this.leafPairCount - 1)
                {
                    perimeter += Math.Abs(an - bn);
                    goto EndIteration;
                }
                double ap = cp.LeafPositions[1, n + 1]; // a[n+1]
                double bp = cp.LeafPositions[0, n + 1]; // b[n+1]

                if (an - bn <= mlc.MinDoseDynamicLeafGap) continue;

                var adjPosDiffB = bn - bp;
                var adjPosDiffA = an - ap;

                // bank B
                if (adjPosDiffB < 0.0) // n+1 is to the right of n
                { // add difference in position
                    perimeter += Math.Min(Math.Abs(adjPosDiffB), Math.Abs(bn - an));
                } else // n+1 is to the left of n
                { // add min between diff in pos and distance between leaves
                    perimeter += Math.Min(Math.Abs(adjPosDiffB), Math.Abs(bp - ap));
                }

                // bank A
                if (adjPosDiffA < 0.0)
                {
                    perimeter += Math.Min(Math.Abs(adjPosDiffA), Math.Abs(an - bn));
                } else
                {
                    perimeter += Math.Min(Math.Abs(adjPosDiffA), Math.Abs(ap - bp));
                }

                EndIteration:
                    perimeter += (2.0 * this.leafThicknessesInMM[n]);
            }

            return perimeter;
        }
        public double getAlpo(ControlPoint cp, MLC mlc)
        {
            double retval = 0.0;
            int openLeafCount = 0;

            for (int i = 0; i < this.leafPairCount; ++i)
            {
                double aLeaf = cp.LeafPositions[1, i];
                double bLeaf = cp.LeafPositions[0, i];

                if (aLeaf - bLeaf <= mlc.MinDoseDynamicLeafGap) continue;

                retval += (aLeaf - bLeaf);
                ++openLeafCount;
            }

            return openLeafCount > 0 ? retval / openLeafCount : retval;
        }

        static string stringifyList(List<double[]> l)
        {
            var _tmp = "";
            foreach (var e in l)
            {
                _tmp += string.Format("{0},{1},{2},{3}", e[0], e[1], e[2], e[3]);
            }
            return _tmp;
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
        {
            var plan = context.PlanSetup;
            Dictionary<string, List<double[]>> results = new Dictionary<string, List<double[]>>();

            // For each beam in the plan, calculate BI, BA and BM parameters
            foreach (var beam in plan.Beams)
            {
                //Millennium120 leaf thicknesses
                this.leafThicknessesInMM = new double[] { 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0 };
                if (beam.MLC.Model == "Varian High Definition 120")
                {
                    this.leafThicknessesInMM = new double[] { 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 2.5, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0 };
                }
                this.leafPairCount = 60; //for both M120 and HD120

                var cpModule = beam.ControlPoints;
                // List<float> allPositions = new List<float>();

                double BI = 0.0;
                double BA = 0.0;
                double BM;
                double ALPO_sum = 0.0;
                results.Add(beam.Id, new List<double[]> { });

                double normalizedMUj = 0.0;
                double prev_normalizedMUj = 0.0;
                List<double> mtws = new List<double>();

                foreach (var cp in cpModule) {
                    mtws.Add(cp.MetersetWeight);
                    normalizedMUj = cp.MetersetWeight - prev_normalizedMUj; // MUj / MU
                    prev_normalizedMUj = cp.MetersetWeight;
                    double AAj = getArea(cp);

                    double perimeter = this.getPerimeter(cp, beam.MLC);

                    BI += (normalizedMUj * (
                        Math.Pow(perimeter, 2.0) / (4.0 * Math.PI * this.getArea(cp))
                    ));
                    BA += (normalizedMUj * this.getArea(cp));
                    ALPO_sum += this.getAlpo(cp, beam.MLC);
                    
                    // if (i % 50 == 0) {
                    //     MessageBox.Show(string.Format("[{0},{1}] - perim: {2}", beam.Id, cp.Index, perimeter));
                    // }
                    // i += 1;
                }
                double areaunion = this.getAreaUnion(beam);
                BM = 1 - (BA / areaunion);

                // MessageBox.Show(string.Format("area union: {0}, mtws: {1}", areaunion, String.Join(", ", mtws.ToArray())));

                results[beam.Id].Add(new double[4] { ALPO_sum / beam.ControlPoints.Count, BI, BA, BM });

            }
            var msg = string.Format(
                "Patient, {0}, {1}\nPlan, {2}, {3}\n\nField,ALPO,BI,BA,BM\n",
                context.Patient.Id,
                context.Patient.Name,
                plan.Id,
                plan.Name
            );
            foreach (var entry in results)
            {
                msg += string.Format(
                    "{0},{1}\n",
                    entry.Key,
                    stringifyList(entry.Value)
                );
            }

            MessageBox.Show(msg);

            Directory.CreateDirectory(this.outputpath);
            File.WriteAllText(this.outputpath + string.Format("{0} - {1}.csv", context.Patient.Name, plan.Id), msg);
        }
    }
}
