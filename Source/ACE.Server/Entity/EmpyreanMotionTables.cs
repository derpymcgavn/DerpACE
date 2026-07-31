using ACE.Entity.Enum;

namespace ACE.Server.Entity
{
    public static class EmpyreanMotionTables
    {
        public const uint MaleFloating = 0x0900020B;
        public const uint FemaleFloating = 0x0900020A;
        public const uint MaleGrounded = 0x0900020E;
        public const uint FemaleGrounded = 0x0900020D;

        public static uint GetGrounded(Gender gender)
            => gender == Gender.Male ? MaleGrounded : FemaleGrounded;

        public static uint GetFloating(Gender gender)
            => gender == Gender.Male ? MaleFloating : FemaleFloating;

        public static bool IsGrounded(uint motionTableId)
            => motionTableId == MaleGrounded || motionTableId == FemaleGrounded;
    }
}