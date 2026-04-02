using System;
using System.Runtime.InteropServices;

namespace Cognex.InSight.Web
{
  public abstract class NativeMethods
  {
    /// <summary>
    /// When used with CombineRgn, specifies a mode indicating how two regions will be combined.
    /// </summary>
    public enum COMBINERGN_MODE
    {
      /// <summary>Creates the intersection of the two combined regions.</summary>
      RGN_AND = 1,

      /// <summary>Creates the union of two combined regions.</summary>
      RGN_OR = 2,

      /// <summary>Creates the union of two combined regions except for any overlapping areas.</summary>
      RGN_XOR = 3,

      /// <summary>Combines the parts of hrgnSrc1 that are not part of hrgnSrc2.</summary>
      RGN_DIFF = 4,

      /// <summary>Creates a copy of the region identified by hrgnSrc1.</summary>
      RGN_COPY = 5,
    }

    /// <summary>
    /// This function combines two regions and stores the result in a third region.
    /// </summary>
    /// <param name="hrgnDest">Handle to a new region with dimensions defined by combining two other regions. (This region must exist before CombineRgn is called.)</param>
    /// <param name="hrgnSrc1">Handle to the first of two regions to be combined.</param>
    /// <param name="hrgnSrc2">Handle to the second of two regions to be combined.</param>
    /// <param name="fnCombineMode">Specifies a mode indicating how the two regions will be combined.</param>
    /// <returns>
    /// The return value specifies the type of the resulting region. 
    /// NULLREGION indicates that the region is empty.
    /// SIMPLEREGION indicates that the region is a single rectangle.
    /// COMPLEXREGION indicates that the region is more than a single rectangle.
    /// ERROR indicates that no region is created. 
    /// </returns>
    [DllImport("Gdi32.dll")]
    public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    /// <summary>
    /// This function deletes a logical pen, brush, font, bitmap, region, or palette, freeing all system resources associated with the object. 
    /// After the object is deleted, the specified handle is no longer valid.
    /// </summary>
    /// <param name="hObject">Handle to a logical pen, brush, font, bitmap, region, or palette.</param>
    /// <returns>
    /// Nonzero indicates success.  Zero indicates that the specified handle is 
    /// not valid or that the handle is currently selected into a device context.
    /// </returns>
    [DllImport("Gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
  }
}
