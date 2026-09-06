using UnityEngine;

namespace RoadDemo
{
    /// <summary>A crew rider's reversible visual fit. One measurement on boarding;
    /// the existing carrying loop reuses the hip offset as the car/aim turns.</summary>
    public sealed class VehicleSeatBinding
    {
        Transform _rider;
        VehicleSeatRig _cabin;
        Vector3 _seat, _scale, _hip;
        bool _fitted;

        public void Place(Transform rider, Animator animator, VehicleSeatRig cabin, Vector3 seat, Quaternion facing)
        {
            if (_rider != rider || _cabin != cabin || _seat != seat)
            {
                Release();
                _rider = rider; _cabin = cabin; _seat = seat; _scale = rider.localScale;
                rider.SetPositionAndRotation(cabin.transform.TransformPoint(seat), facing);
                _fitted = cabin.FitSeated(rider, animator, seat);
                if (_fitted) _hip = rider.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
            }
            rider.rotation = facing;
            rider.position = cabin.transform.TransformPoint(seat + (_fitted ? Vector3.up * VehicleSeatRig.CushionAboveRoot : Vector3.zero));
            if (_fitted) rider.position -= rider.TransformVector(_hip);
        }

        public void Release()
        {
            if (_rider) _rider.localScale = _scale;
            _rider = null; _cabin = null; _fitted = false;
        }
    }
}
