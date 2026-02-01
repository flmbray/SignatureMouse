using SignatureMouse.Models;

namespace SignatureMouse.Replay;

internal interface IPlopSelector
{
    PlopResult? SelectPlacement(SignaturePath signature);
}
