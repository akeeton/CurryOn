﻿namespace CurryOn.Core

open CurryOn.Common
open FSharp.Control
open System
open System.Collections.Concurrent
open System.IO
open System.Reflection

type IComponent = 
    interface end

[<Measure>] 
type TypeName = 
    static member New: string -> string<TypeName> = fun value -> {Value = value}

type IContainer =
    abstract member InstanceOf<'c when 'c :> IComponent> : unit   -> Operation<'c,exn>
    abstract member InstanceOf<'c when 'c :> IComponent> : obj [] -> Operation<'c,exn>
    abstract member RegisterSingleton<'c when 'c :> IComponent> : 'c -> Operation<unit,exn>
    abstract member RegisterFactory<'c when 'c :> IComponent> : (unit -> 'c) -> Operation<unit,exn>
    abstract member RegisterConstructor<'c when 'c :> IComponent> : ConstructorInfo -> Operation<unit,exn>
    abstract member GetNamedType : string<TypeName> -> Operation<Type,exn>

type TypeInjector<'c when 'c :> IComponent> =
| Singleton of 'c
| Factory of (unit -> 'c)
| Constructor of ConstructorInfo
with
    member this.Instance () =
        match this with
        | Singleton instance -> instance
        | Factory factory -> factory ()
        | Constructor ctor -> ctor.Invoke [||] |> unbox<'c>
    member this.Instance parameters =
        match this with
        | Singleton instance -> instance
        | Factory factory -> factory ()
        | Constructor ctor -> ctor.Invoke parameters |> unbox<'c>



module Context =
    let private typeInjectors = new ConcurrentDictionary<Type, TypeInjector<IComponent>>()
    let private binaryDirectories = [Environment.CurrentDirectory; Configuration.Common.AssemblySearchPath;] |> List.map DirectoryInfo

    let private registerTypeInjector<'c when 'c :> IComponent> (injector: TypeInjector<IComponent>) = 
        operation {
            typeInjectors.AddOrUpdate(typeof<'c>, injector, (fun _ _ -> injector)) |> ignore
        }

    let registerSingleton<'c when 'c :> IComponent> (singletonInstance: 'c) =
        singletonInstance :> IComponent |> Singleton |> registerTypeInjector<'c>

    let registerFactory<'c when 'c :> IComponent> (factory: unit -> 'c) =
        (fun () -> factory () :> IComponent) |> Factory |> registerTypeInjector<'c>

    let registerConstructor<'c when 'c :> IComponent> (ctor: ConstructorInfo) =
        ctor |> Constructor |> registerTypeInjector<'c>

    let private loadAssemblies = memoize <| fun () ->
        operation {
            return AppDomain.CurrentDomain.GetAssemblies()
                   |> Seq.append (binaryDirectories 
                                  |> Seq.collect (fun dir -> dir.EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)) 
                                  |> Seq.tryMap (fun dll -> Assembly.LoadFile(dll.FullName)))
                   |> Seq.distinctBy (fun assembly -> assembly.FullName)
                   |> Seq.toList
        }         
        
    let internal getAllKnownTypes = memoize <| fun () ->
        operation {
            let! assembiles = loadAssemblies()
            return assembiles |> Seq.collect (fun assembly -> assembly.GetTypes())
                              |> Seq.distinctBy (fun t -> t.FullName)
                              |> Seq.toList
        }
    
    let internal isNamedType (knownType: Type) (typeName: string<TypeName>) =
        knownType.AssemblyQualifiedName = typeName.Value ||
        (sprintf "%s.%s" knownType.Namespace knownType.Name) = typeName.Value ||
        knownType.Name = typeName.Value
    
    let getNamedType (typeName: string<TypeName>) =
        operation {
            let! knownTypes = getAllKnownTypes ()
            return!
                match knownTypes |> List.tryFind (fun knownType -> isNamedType knownType typeName) with
                | Some foundType -> Result.success foundType
                | None -> Result.failure [exn <| sprintf "No Type with Name Matching '%s' Found" typeName.Value]
        }
            
    let getInjectionTypeCandidates = memoize <| fun (injectionType: Type) ->
        operation {
            let! knownTypes = getAllKnownTypes ()
            return knownTypes |> List.filter injectionType.IsAssignableFrom            
        }

    let private getConstructors = memoize <| fun (types: Type list, condition) ->  // Note: Must use tuples for multi-parameter memoization
        operation {
            return types |> List.map (fun candidateType -> 
                (candidateType, candidateType.GetConstructors() 
                                |> Seq.tryFind (fun ctor -> ctor.GetParameters() |> condition)))
        }

    let private getSingleParameterConstructors = memoize <| fun (types: Type list, parameterType) -> 
        getConstructors (types, (fun constructorParameters -> 
            constructorParameters.Length = 1 
            && constructorParameters.[0].ParameterType.IsAssignableFrom(parameterType)))

    let getDefaultConstructors = memoize <| fun (types: Type list) ->
        getConstructors (types, Seq.isEmpty)

    let getContainerConstructors = memoize <| fun (types: Type list) ->
        getSingleParameterConstructors (types, typeof<IContainer>)
     
    let getConfigurationConstructors = memoize <| fun (types: Type list) ->
        getSingleParameterConstructors (types, typeof<IConfiguration>)

    let getContructorsWithParameters (types: Type list) (parameters: obj []) =
        getConstructors (types, (fun constructorParameters -> 
            if parameters.Length = constructorParameters.Length
            then let foundParameterTypes = constructorParameters |> Array.map (fun p -> p.ParameterType)
                 let searchParameterTypes = parameters |> Array.map (fun p -> p.GetType())
                 [|0..parameters.Length|] |> Array.forall (fun index -> foundParameterTypes.[index] = searchParameterTypes.[index])
            else false))

    let rec getBestMatchForTypeInjection<'injectionType when 'injectionType :> IComponent> (optionalParameters: obj [] option) =
        operation {
            let injectionType = typeof<'injectionType>
            let! candidateTypes = getInjectionTypeCandidates injectionType
            match optionalParameters with
            | Some parameters ->
                let! candidateConstructors = getContructorsWithParameters candidateTypes parameters
                let candidatesWithConstructors =
                    candidateConstructors 
                    |> List.filter (fun (_,ctor) -> ctor.IsSome)
                    |> List.map (fun (t, ctor) -> (t, ctor.Value))

                if candidatesWithConstructors |> List.isEmpty
                then return! Failure [exn <| sprintf "No Type Implementing %s with parameters [%s] Could be Found" injectionType.Name (String.Join(", ", (parameters |> Array.map (fun p -> p.GetType().Name))))]
                else let (bestMatch, ctor) = candidatesWithConstructors.Head
                     return Factory <| fun () -> parameters |> ctor.Invoke |> unbox<IComponent>
            | None ->
                let! candidateDefaultConstructors = getDefaultConstructors candidateTypes
                let candidatesWithDefaultConstructors = 
                    candidateDefaultConstructors 
                    |> List.filter (fun (_, ctor) -> ctor.IsSome)
                    |> List.map (fun (t, ctor) -> (t, ctor.Value))

                if candidatesWithDefaultConstructors |> List.isEmpty
                then let! candidateContainerConstructors = getContainerConstructors candidateTypes
                     let candidatesWithContainerConstructors =
                         candidateContainerConstructors
                         |> List.filter (fun (_, ctor) -> ctor.IsSome)
                         |> List.map (fun (t, ctor) -> (t, ctor.Value))

                     if candidatesWithContainerConstructors |> List.isEmpty
                     then let! candidateConfigurationConstructors = getConfigurationConstructors candidateTypes
                          let candidatesWithConfigurationConstructors =
                              candidateConfigurationConstructors
                              |> List.filter (fun (_, ctor) -> ctor.IsSome)
                              |> List.map (fun (t, ctor) -> (t, ctor.Value))

                          if candidatesWithConfigurationConstructors |> List.isEmpty
                          then return! Failure [exn <| sprintf "No Type Implementing %s Could be Found" injectionType.Name]
                          else let (bestMatch, ctor) = candidatesWithContainerConstructors.Head
                               return Factory <| fun () -> ctor.Invoke [| Configuration.Common |] |> unbox<IComponent>
                     else let (bestMatch, ctor) = candidatesWithContainerConstructors.Head
                          return Factory <| fun () -> ctor.Invoke [| Container |] |> unbox<IComponent>
                else let (bestMatch, ctor) = candidatesWithDefaultConstructors.Head
                     return Factory <| fun () -> ctor.Invoke [||] |> unbox<IComponent>
        }

    and Container = 
        {new IContainer with
            member __.InstanceOf<'c when 'c :> IComponent> () =
                operation {
                    let injector = typeInjectors.GetOrAdd(typeof<'c>, (fun _ -> getBestMatchForTypeInjection<'c> None |> Operation.returnOrFail))
                    return injector.Instance() |> unbox<'c>
                }
            member __.InstanceOf<'c when 'c :> IComponent> (parameters: obj []) =
                operation {
                    let injector = typeInjectors.GetOrAdd(typeof<'c>, (fun _ -> getBestMatchForTypeInjection<'c> (parameters |> Some) |> Operation.returnOrFail))
                    return injector.Instance parameters |> unbox<'c>
                }
            member __.RegisterSingleton singletonInstance = registerSingleton singletonInstance
            member __.RegisterFactory factory = registerFactory factory
            member __.RegisterConstructor ctor = registerConstructor ctor
            member __.GetNamedType typeName = getNamedType typeName
        }