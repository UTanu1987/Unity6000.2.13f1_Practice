using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Beaver.Script.Player
{
    public class PlayerMove : MonoBehaviour
    {
        [SerializeField, Header("ジャンプ力")]
        private float _jumpForce = 5.0f;
        private bool _isJumping = false;

        [SerializeField, Header("移動速度")]
        private float _moveSpeed = 5.0f;

        [Header("重力加速度"), SerializeField]
        private float _gravity = 15;

        [Header("落下時の速さ制限（Infinityで無制限）"), SerializeField]
        private float _fallSpeed = 10;

        [Header("落下の初速"), SerializeField]
        private float _initFallSpeed = 2;

        [Header("カメラ"), SerializeField]
        private Camera _targetCamera = null;

        private InputSystem_Actions _inputSystem_Actions = null;

        private Transform _transform;
        private CharacterController _characterController = null;
        private Animator _animator = null;

        private Vector2 _inputMove;
        private float _verticalVelocity;
        private float _turnVelocity;
        private bool _isGroundedPrev;

        private bool _isGround;

        // Rayの長さ
        [SerializeField] private float _rayLength = 1f;

        // Rayをどれくらい身体にめり込ませるか
        [SerializeField] private float _rayOffset;

        // Rayの判定に用いるLayer
        [SerializeField] private LayerMask _layerMask = default;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Awake()
        {
            _transform = transform;
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _inputSystem_Actions = new InputSystem_Actions();

            // ActionMaps[Player]の中の[Jump]というActionに紐づくイベントリスナーを登録
            _inputSystem_Actions.Player.Jump.performed += OnJump;

            // Moveアクションにリスナーを追加
            _inputSystem_Actions.Player.Move.performed += OnMove;
            _inputSystem_Actions.Player.Move.canceled += OnMoveCanceled;
        }

        private void OnEnable()
        {
            // Inputアクションを有効化
            _inputSystem_Actions.Enable();
        }

        private void OnDisable()
        {
            // Inputアクションを無効化
            _inputSystem_Actions.Disable();
        }

        //Moveの入力を受け取り、Rigidbodyを使ってボールを動かす
        private void FixedUpdate()
        {
            _isGround = CheckGrounded();
            FallPlayer();
            MovePlayer();

        }

        private bool CheckGrounded()
        {
            // 放つ光線の初期位置と姿勢
            // 若干身体にめり込ませた位置から発射しないと正しく判定できない時がある
            var ray = new Ray(origin: transform.position + Vector3.up * _rayOffset, direction: Vector3.down);

            // Raycastがhitするかどうかで判定
            // レイヤの指定を忘れずに
            return Physics.Raycast(ray, _rayLength, _layerMask);
        }

        // Debug用にRayを可視化する
        private void OnDrawGizmos()
        {
            // 接地判定時は緑、空中にいるときは赤にする
            Gizmos.color = _isGround ? Color.green : Color.red;
            Gizmos.DrawRay(transform.position + Vector3.up * _rayOffset, Vector3.down * _rayLength);
        }

        private void MovePlayer()
        {
            // カメラの向き（角度[deg]）取得
            var cameraAngleY = _targetCamera.transform.eulerAngles.y;

            // 操作入力と鉛直方向速度から、現在速度を計算
            var moveVelocity = new Vector3(
                _inputMove.x * _moveSpeed,
                _verticalVelocity,
                _inputMove.y * _moveSpeed
            );

            // カメラの角度分だけ移動量を回転
            moveVelocity = Quaternion.Euler(0, cameraAngleY, 0) * moveVelocity;

            // 現在フレームの移動量を移動速度から計算
            var moveDelta = moveVelocity * Time.deltaTime;

            // CharacterControllerに移動量を指定し、オブジェクトを動かす
            _characterController.Move(moveDelta);

            if (_inputMove != Vector2.zero)
            {
                if (_isGround)
                {
                    var totalSpeed = Mathf.Abs(_inputMove.x) + Mathf.Abs(_inputMove.y);
                    //Debug.Log(totalSpeed);
                    _animator.SetFloat("Speed", totalSpeed);
                }


                // 移動入力がある場合は、振り向き動作も行う

                // 操作入力からy軸周りの目標角度[deg]を計算
                var targetAngleY = -Mathf.Atan2(_inputMove.y, _inputMove.x)
                    * Mathf.Rad2Deg + 90;
                // カメラの角度分だけ振り向く角度を補正
                targetAngleY += cameraAngleY;

                // イージングしながら次の回転角度[deg]を計算
                var angleY = Mathf.SmoothDampAngle(
                    _transform.eulerAngles.y,
                    targetAngleY,
                    ref _turnVelocity,
                    0.1f
                );

                // オブジェクトの回転を更新
                _transform.rotation = Quaternion.Euler(0, angleY, 0);
            }
        }

        private void FallPlayer()
        {
            if (_isGround && !_isGroundedPrev)
            {
                // 着地する瞬間に落下の初速を指定しておく
                _verticalVelocity = -_initFallSpeed;
                _animator.SetBool("isJump", false);
            }
            else if (!_isGround)
            {
                // 空中にいるときは、下向きに重力加速度を与えて落下させる
                _verticalVelocity -= _gravity * Time.deltaTime;

                // 落下する速さ以上にならないように補正
                if (_verticalVelocity < -_fallSpeed) _verticalVelocity = -_fallSpeed;
            }

            _isGroundedPrev = _isGround;
        }

        /// <summary>
        /// 移動Action(PlayerInput側から呼ばれる)
        /// </summary>
        public void OnMove(InputAction.CallbackContext context)
        {
            // 入力値を保持しておく
            _inputMove = context.ReadValue<Vector2>();
        }

        /// <summary>
        /// 入力がなくなったら呼ばれる
        /// </summary>
        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            _inputMove = Vector2.zero;
            _animator.SetFloat("Speed", 0);
        }

        /// <summary>
        /// ジャンプAction(PlayerInput側から呼ばれる)
        /// </summary>
        public void OnJump(InputAction.CallbackContext context)
        {
            // ボタンが押された瞬間かつ着地している時だけ処理
            if (context.performed && !_isGround) return;

            // 鉛直上向きに速度を与える
            _verticalVelocity = _jumpForce;

            _animator.SetBool("isJump", true);
        }
    }
}

