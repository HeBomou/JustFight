using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace JustFight {

    [UpdateBefore (typeof (BuildPhysicsWorld))]
    class TankHullSystem : JobComponentSystem {

        [BurstCompile]
        struct InstantiateHealthBarJob : IJobForEachWithEntity<HealthBarPrefab> {
            public EntityCommandBuffer.Concurrent ecb;
            public void Execute (Entity entity, int entityInQueryIndex, [ReadOnly] ref HealthBarPrefab healthBarPrefabCmpt) {
                var healthBarEntity = ecb.Instantiate (entityInQueryIndex, healthBarPrefabCmpt.entity);
                ecb.SetComponent (entityInQueryIndex, healthBarEntity, new TankHullToFollow { entity = entity, offset = new float3 (-1.8f, 0, 0) });
                ecb.RemoveComponent<HealthBarPrefab> (entityInQueryIndex, entity);
                ecb.AddComponent (entityInQueryIndex, entity, new HealthBarInstance { entity = healthBarEntity });
            }
        }

        [BurstCompile]
        struct DestroyTankJob : IJobForEachWithEntity<Health, HealthBarInstance, TankTurretInstance> {
            public EntityCommandBuffer.Concurrent ecb;
            public void Execute (Entity entity, int entityInQueryIndex, [ReadOnly] ref Health healthCmpt, [ReadOnly] ref HealthBarInstance healthBarInstanceCmpt, [ReadOnly] ref TankTurretInstance turretInstanceCmpt) {
                if (healthCmpt.value <= 0) {
                    ecb.DestroyEntity (entityInQueryIndex, healthBarInstanceCmpt.entity);
                    ecb.DestroyEntity (entityInQueryIndex, turretInstanceCmpt.entity);
                    ecb.DestroyEntity (entityInQueryIndex, entity);
                }
            }
        }

        [BurstCompile]
        struct JumpTankJob : IJobForEach<JumpInput, JumpState, PhysicsVelocity> {
            public float dT;
            public void Execute ([ReadOnly] ref JumpInput jumpInputCmpt, ref JumpState jumpStateCmpt, ref PhysicsVelocity velocityCmpt) {
                if (jumpStateCmpt.leftRecoveryTime > 0) {
                    jumpStateCmpt.leftRecoveryTime -= dT;
                } else {
                    if (jumpInputCmpt.isJump) {
                        velocityCmpt.Linear.y += jumpStateCmpt.speed;
                        jumpStateCmpt.leftRecoveryTime = jumpStateCmpt.recoveryTime;
                    }
                }
            }
        }

        [BurstCompile]
        struct MoveTankJob : IJobForEach<MoveSpeed, MoveInput, Rotation, PhysicsVelocity> {
            public void Execute ([ReadOnly] ref MoveSpeed moveSpeedCmpt, [ReadOnly] ref MoveInput moveInputCmpt, ref Rotation rotationCmpt, ref PhysicsVelocity velocityCmpt) {
                var dir = moveInputCmpt.dir;
                if (dir.x != 0 || dir.z != 0)
                    rotationCmpt.Value = quaternion.LookRotation (dir, math.up ());
                dir *= moveSpeedCmpt.value;
                dir.y = velocityCmpt.Linear.y;
                velocityCmpt.Linear = dir;
            }
        }

        BeginInitializationEntityCommandBufferSystem entityCommandBufferSystem;
        protected override void OnCreate () {
            entityCommandBufferSystem = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem> ();
        }
        protected override JobHandle OnUpdate (JobHandle inputDeps) {
            var instantiateJobHandle = new InstantiateHealthBarJob { ecb = entityCommandBufferSystem.CreateCommandBuffer ().ToConcurrent () }.Schedule (this, inputDeps);
            var destroyTankJobHandle = new DestroyTankJob { ecb = entityCommandBufferSystem.CreateCommandBuffer ().ToConcurrent () }.Schedule (this, inputDeps);
            var jumpTankJobHandle = new JumpTankJob { dT = Time.DeltaTime }.Schedule (this, inputDeps);
            var moveTankJobHandle = new MoveTankJob ().Schedule (this, jumpTankJobHandle);
            entityCommandBufferSystem.AddJobHandleForProducer (instantiateJobHandle);
            entityCommandBufferSystem.AddJobHandleForProducer (destroyTankJobHandle);
            return JobHandle.CombineDependencies (instantiateJobHandle, destroyTankJobHandle, moveTankJobHandle);
        }
    }
}