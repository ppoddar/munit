# munit

##  a simple unit test framework

  
munit takes an assembly and runs the test cases in them.
The results and failures with exeption stack trace are printed on the console.
  
A test class must have the following
  
   1. a `[TestClass]` attribute
   
   2. a no-arg public constructor
   
   3. onen or more public methods with `[TestCase]` attribute
  
Additionally, a test class can have
   
    4. a static method with `[OneTimeSetUp]` attribute

       5. a static method with `[OneTimeTearDown]` attribute

       6. an instance method with `[SetUp]` attribute

       7. an instance method with `[TearDown]` attribute
  
  
## Theory of operation

    1. munit loads the assembly 
  
    2. reades all exported types in the assembly that are annotated with `[TestClass]`.
  
    3. Takes each test class `T`, 

       a. runs static `[OneTimeSetUp]` method, 

       b. for each `[TestCase]` method  `m`
       
        1. Create an instance `t` of `T` 
        
        2. runs `[SetUp]`, if any, on `t`
       
        3 Runs `m` on `t`
       
        4 Runs `[TearDown]`, if any, on `t`
    
    3 runs static `[OneTimeTearDown]`, if any
   
  
## Basic Steps
  
   A1. Annotate a class with [TestClass]
 
   A2. Annotate one or more methods with [TestCase] 
  
### Optional steps:

  A3. Annotate one static method with [OneTimeSetUp]     
  
  A4. Annotate one static method with [OneTimeTearDown] 
  
  A5. Annotate one instance method with [SetUp]           
  
  A6. Annotate one instance method with [TearDown]        
  
  B. Compile the test classes into a test.dll.
