using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Swiftlet.DataModels.Implementations;
using Swiftlet.Goo;
using Swiftlet.Params;
using Swiftlet.Util;

namespace Swiftlet.Components
{
    public class ResponseToMultipart : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeconstructHttpResponse class.
        /// </summary>
        public ResponseToMultipart()
          : base("Response to multipart", "RM",
              "Response to multipart",
              NamingUtility.CATEGORY, NamingUtility.SEND)
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new HttpWebResponseParam(), "Response", "R", "Http Web response to deconstruct", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Version", "V", "The HTTP message version. The default is 1.1", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Status", "S", "Http status code", GH_ParamAccess.item);
            pManager.AddTextParameter("Reason", "R", "The reason phrase which typically is sent by servers together with the status code", GH_ParamAccess.item);
            pManager.AddParameter(new HttpHeaderParam(), "Headers", "H", "The collection of HTTP response headers", GH_ParamAccess.list);
            pManager.AddBooleanParameter("IsSuccess", "iS", "Indicates if the HTTP response was successful", GH_ParamAccess.item);
            pManager.AddGenericParameter("Parts", "P", "Multipart parts", GH_ParamAccess.list);

            //pManager.AddTextParameter("Content", "C", "Response content", GH_ParamAccess.item);
            //pManager.AddParameter(new ByteArrayParam(), "Byte Array", "A", "Response data as byte array", GH_ParamAccess.item);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            HttpWebResponseGoo goo = null;
            DA.GetData(0, ref goo);
            HttpResponseDTO dto = goo.Value;

            string boundary = null;

            List<MultipartPartGoo> multipartGoos = new List<MultipartPartGoo>();


            if(MultipartUtils.TryParseMultipartBoundaryFromHeader(dto.Headers, out boundary))
            {
                var memStream = new MemoryStream(dto.Bytes);
                HttpMultipart multipart = new HttpMultipart(memStream, boundary);

                IEnumerable<HttpMultipartBoundary> parts = multipart.GetBoundaries();

                //IList<IAssetPart> assets = new List<IAssetPart>();
                string json = null;

                foreach (HttpMultipartBoundary part in parts)
                {
                    string name = part.Name;
                    string fileName = part.Filename;
                    string contentType = part.ContentType;
                    HttpMultipartSubStream substream = part.Value;

                    if (name.ToLower() == "content")
                    {
                        json = new StreamReader(substream).ReadToEnd();
                    }
                    else
                    {
                        byte[] b;
                        using (BinaryReader br = new BinaryReader(substream))
                        {
                            b = br.ReadBytes((int)substream.Length);
                        }

                        var metaData = new Dictionary<string, string>()
                        {
                            {"Content-Type", contentType},
                            {"name", name},
                            {"filename", fileName},
                        };

                        MultipartPartGoo mpg = new MultipartPartGoo()
                        {
                            ByteArray = b,
                            Metadata = metaData
                        };
                        multipartGoos.Add(mpg);
                        //AssetPart p = new AssetPart(substream, fileName, contentType, name);
                        //assets.Add(p);
                    }
                }

            }


            DA.SetData(0, dto.Version);
            DA.SetData(1, dto.StatusCode);
            DA.SetData(2, dto.ReasonPhrase);
            DA.SetDataList(3, dto.Headers.Select(h => new HttpHeaderGoo(h)));
            DA.SetData(4, dto.IsSuccessStatusCode);
            DA.SetDataList(5, multipartGoos);
            //DA.SetData(6, new ByteArrayGoo(dto.Bytes));
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Properties.Resources.Icons_deconstruct_response_24x24;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("3c668a02-7873-439b-afbc-21c8fe660e86"); }
        }
    }


    public class UnpackMultipart : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeconstructHttpResponse class.
        /// </summary>
        public UnpackMultipart()
          : base("Unpack multipart", "UM",
              "Unpack multipart",
              NamingUtility.CATEGORY, NamingUtility.SEND)
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Multpiart Part", "MP", "Multipart part", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Metadata keys", "Mk", "Metadata keys", GH_ParamAccess.list);
            pManager.AddTextParameter("Metadata vals", "Mv", "Metadata vals", GH_ParamAccess.list);
            pManager.AddParameter(new ByteArrayParam(), "Byte Array", "A", "Multpart part data as byte array", GH_ParamAccess.item);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MultipartPartGoo inputGoo = null;
            DA.GetData(0, ref inputGoo);

            var keys = inputGoo.Metadata.Keys.ToList();
            var vals = inputGoo.Metadata.Values.ToList();

            DA.SetDataList(0, keys);
            DA.SetDataList(1, vals);
            DA.SetData(2, new ByteArrayGoo(inputGoo.ByteArray));
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Properties.Resources.Icons_deconstruct_response_24x24;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a63e6a44-ff0b-4e65-b50d-f894b5050225"); }
        }
    }


    public class MultipartPartGoo
    {
        public byte[] ByteArray { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
    }

}