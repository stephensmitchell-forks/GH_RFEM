﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Rhino.Geometry;
using Dlubal.RFEM5;
using System.Windows.Forms;

namespace GH_RFEM
{
    public class RFEM_Line_Write : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public RFEM_Line_Write()
          : base("Line RFEM", "RFEM Ln Write",
              "Create RFEM lines from Rhino lines",
              "RFEM", "Write")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        /// 
        // define all variables where input data is stored
        List<Rhino.Geometry.Curve> rhinoCurvesInput = new List<Curve>();
        bool run = false;
        double segmentLengthInput = 1;
        string commentsInput = "";
        Dlubal.RFEM5.LineSupport rfemLineSupportInput = new Dlubal.RFEM5.LineSupport();


        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Use the pManager object to register your input parameters.
            // You can often supply default values when creating parameters.
            // All parameters must have the correct access type. If you want 
            // to import lists or trees of values, modify the ParamAccess flag.
            pManager.AddCurveParameter("Curve", "Curve", "Input Rhino curves you want to create as RFEM lines", GH_ParamAccess.list);
            pManager.AddNumberParameter("Segment length", "MaxSegmentLength[m]", "Any splines/circles/arcs will be simplified as segments with maximum length described in this parameter", GH_ParamAccess.item, segmentLengthInput);
            pManager.AddGenericParameter("RFEM line support", "Line support", "Leave empty if no support, use 'Line Support' node to generate support", GH_ParamAccess.item);
            pManager.AddTextParameter("Text to be written in RFEM comments field", "Comment", "Text written in RFEM comments field", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Run", "Toggle", "Toggles whether the lines are written to RFEM", GH_ParamAccess.item, run);


            // If you want to change properties of certain parameters, 
            // you can use the pManager instance to access them by index:
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        /// 
        // defining variables for output
        bool writeSuccess = false;
        List<Dlubal.RFEM5.Line> RfemLines = new List<Dlubal.RFEM5.Line>();

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Use the pManager object to register your output parameters.
            pManager.AddGenericParameter("RFEM lines", "RfLn", "Generated RFEM lines", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Success", "Success", "Returns 'true' if nodes successfully writen, otherwise it is 'false'", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // First, we need to retrieve all data from the input parameters.
            // We'll start by declaring variables and assigning them starting values.

            // Then we need to access the input parameters individually. 
            // When data cannot be extracted from a parameter, we should abort this method.
            if (!DA.GetDataList<Rhino.Geometry.Curve>(0, rhinoCurvesInput)) return;
            DA.GetData(1, ref segmentLengthInput);
            DA.GetData(2, ref rfemLineSupportInput);
            DA.GetData(3, ref commentsInput);
            DA.GetData(4, ref run);


            // The actual functionality will be in a method defined below. This is where we run it
            if (run == true)
            {
                //clears and resets all output parameters:
                RfemLines.Clear();
                writeSuccess = false;

                RfemLines = CreateRfemLines(rhinoCurvesInput, rfemLineSupportInput, commentsInput);
                DA.SetData(1, writeSuccess);
            }
            else
            {
                DA.SetData(1, false);
            }

            // Finally assign the processed data to the output parameter.
            DA.SetDataList(0, RfemLines);

            // clear and reset all input parameters
            rhinoCurvesInput.Clear();
            commentsInput = "";
            rfemLineSupportInput = new Dlubal.RFEM5.LineSupport();
        }

        private List<Dlubal.RFEM5.Line> CreateRfemLines(List<Rhino.Geometry.Curve> Rh_Crv, Dlubal.RFEM5.LineSupport rfemLineSupportMethodIn, string commentsListMethodIn)
        {
            //start by reducing the input curves to simple lines with start/end points
            List<Rhino.Geometry.Curve> RhSimpleLines = new List<Rhino.Geometry.Curve>();

            foreach (Rhino.Geometry.Curve RhSingleCurve in Rh_Crv)
            {
                
                if (RhSingleCurve.IsPolyline() )
                {
                    if (RhSingleCurve.SpanCount==1)
                    {
                        RhSimpleLines.Add(RhSingleCurve);
                    }
                    else
                    {
                        foreach (Rhino.Geometry.Curve explodedLine in RhSingleCurve.DuplicateSegments())
                        {
                            RhSimpleLines.Add(explodedLine);
                        }
                    }
                }

                else
                {
                    
                    foreach (Rhino.Geometry.Curve explodedLine in RhSingleCurve.ToPolyline(0, 0, 3.14, 1, 0, 0, 0, segmentLengthInput, true).DuplicateSegments())
                    {
                        RhSimpleLines.Add(explodedLine);
                    }

                }
                
            }

            //defining variables needed to store geometry and RFEM info
            Rhino.Geometry.Point3d startPoint;
            Rhino.Geometry.Point3d endPoint;
            Dlubal.RFEM5.Line[] RfemLineArray = new Dlubal.RFEM5.Line[RhSimpleLines.Count+1];
            Dlubal.RFEM5.Node[] RfemNodeArray = new Dlubal.RFEM5.Node[RhSimpleLines.Count * 2+2];

            // Gets interface to running RFEM application.
            IApplication app = Marshal.GetActiveObject("RFEM5.Application") as IApplication;
            // Locks RFEM licence
            app.LockLicense();

            // Gets interface to active RFEM model.
            IModel model = app.GetActiveModel();

            // Gets interface to model data.
            IModelData data = model.GetModelData();

            // Gets Max line Number
            int currentNewLineNo = 1;
            int totalLinesCount = data.GetLines().Count();
            if (totalLinesCount != 0)
            {
                int lastLineNo = data.GetLine(totalLinesCount - 1, ItemAt.AtIndex).GetData().No;
                currentNewLineNo = lastLineNo + 1;
            }

            // Gets Max node Number
            int currentNewNodeNo = 1;
            int totalNodesCount = data.GetNodes().Count();
            if (totalNodesCount != 0)
            {
                int lastNodeNo = data.GetNode(totalNodesCount - 1, ItemAt.AtIndex).GetData().No;
                currentNewNodeNo = lastNodeNo + 1;
            }

            // Gets Max line support number
            int currentNewLineSupportNo = 1;
            int totalLineSupportsCount = data.GetLineSupports().Count();
            if (totalLineSupportsCount != 0)
            {
                int lastLineSupportNo = data.GetLineSupport(totalLineSupportsCount - 1, ItemAt.AtIndex).GetData().No;
                currentNewLineSupportNo = lastLineSupportNo + 1;
            }


            // modification
            // Sets all objects to model data.
            data.PrepareModification();

            ///This version writes nodes one-by-one because the data.SetNodes() for
            ///array appears not to be working
            try
            {

                //cycling through all lines and creating RFEM objects;
                int nodeCount = 1;
                int lineCount = 1;
                string createdLinesList = "";

                for (int i = 1; i < RhSimpleLines.Count+1; i++)
                {
                    startPoint = RhSimpleLines[i - 1].PointAtStart;
                    endPoint = RhSimpleLines[i - 1].PointAtEnd;

                    RfemNodeArray[nodeCount].No = currentNewNodeNo;
                    RfemNodeArray[nodeCount].X = startPoint.X;
                    RfemNodeArray[nodeCount].Y = startPoint.Y;
                    RfemNodeArray[nodeCount].Z = startPoint.Z;

                    RfemNodeArray[nodeCount + 1].No = currentNewNodeNo + 1;
                    RfemNodeArray[nodeCount + 1].X = endPoint.X;
                    RfemNodeArray[nodeCount + 1].Y = endPoint.Y;
                    RfemNodeArray[nodeCount + 1].Z = endPoint.Z;


                    data.SetNode(RfemNodeArray[nodeCount]);
                    data.SetNode(RfemNodeArray[nodeCount + 1]);


                    RfemLineArray[lineCount].No = currentNewLineNo;
                    RfemLineArray[lineCount].Type = LineType.PolylineType;
                    RfemLineArray[lineCount].Comment = commentsListMethodIn;
                    RfemLineArray[lineCount].NodeList = $"{RfemNodeArray[nodeCount].No}, {RfemNodeArray[nodeCount + 1].No}";

                    data.SetLine(RfemLineArray[lineCount]);

                    if (i == RhSimpleLines.Count)
                    {
                        createdLinesList = createdLinesList + currentNewLineNo.ToString();
                    }
                    else
                    {
                        createdLinesList = createdLinesList + currentNewLineNo.ToString() + ",";
                    }

                    nodeCount = nodeCount + 2;
                    lineCount++;

                    currentNewLineNo++;
                    currentNewNodeNo = currentNewNodeNo + 2;
                }

                //addition of line supports
                rfemLineSupportMethodIn.No = currentNewLineSupportNo;
                rfemLineSupportMethodIn.LineList = createdLinesList;
                data.SetLineSupport(ref rfemLineSupportMethodIn);


                // finish modification - RFEM regenerates the data
                data.FinishModification();
            }

            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Releases interface to RFEM model.
            model = null;

            // Unlocks licence and releases interface to RFEM application.
            if (app != null)
            {
                app.UnlockLicense();
                app = null;
            }

            // Cleans Garbage Collector and releases all cached COM interfaces.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();

            ///the lines below outputs created RFEM nodes in output parameter
            ///current funcionality does not use this
            ///it uses a custom class (written within this project) RfemNodeType to wrap the Dlubal.RFEM5.Node objects.
            
     
            List<Dlubal.RFEM5.Line> RfemLineList = RfemLineArray.OfType<Dlubal.RFEM5.Line>().ToList(); // this isn't going to be fast.

            //output 'success' as true 
            writeSuccess = true;
            return RfemLineList;


        }



        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return GH_RFEM.Properties.Resources.icon_line;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("bad6a0dd-e550-4d1e-9c75-15c03d6f73a1"); }
        }
    }
}
