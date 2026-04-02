using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Cognex.InSight.Remoting.Serialization
{
  public enum InspectEdgePointType
  {
    Edge = 0,
    Extreme,
    Defect
  }

  [CvsSerializable(JsonName = "InspectEdgePoints")]
  public class CvsCogInspectEdgePoints : CvsCogPointList
  {
    [JsonProperty(PropertyName = "pointType", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public InspectEdgePointType PointType { get; set; }
  }
}
