using UnityEngine;
using UnityEngine.InputSystem;

public sealed class WeaponRecipeExamples : MonoBehaviour
{
    [SerializeField] private Transform firePoint;
    private WeaponRuntime weapon;
    private void Start() { weapon=WeaponComposer.Compose(WeaponDescription.Interpret("A flaming knife"),transform); weapon.Owner=transform; }
    private void Update()
    {
        if(weapon!=null && Mouse.current!=null && Mouse.current.leftButton.isPressed)
        { var source=firePoint!=null ? firePoint : transform; weapon.TryUse(source.position,source.forward); }
    }
}
